using System.Collections.Concurrent;
using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public class BankrollService : IBankrollService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly decimal _initialBankroll;
    private readonly decimal _maxStakePct;
    private readonly decimal _dailyLossPct;
    private readonly decimal _stopLossPct;
    private readonly decimal _maxExposurePct;

    // Per-user mutable bankroll state. Loaded lazily on first access per user
    // rather than eagerly at startup, since this is a Singleton shared across
    // every authenticated user's requests.
    private readonly ConcurrentDictionary<int, BankrollState> _states = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public BankrollService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory    = scopeFactory;
        _initialBankroll = config.GetValue<decimal>("BettingSettings:InitialBankroll", 10_000m);
        _maxStakePct     = config.GetValue<decimal>("BettingSettings:MaxStakePercent", 0.03m);
        _dailyLossPct    = config.GetValue<decimal>("BettingSettings:DailyLossLimitPercent", 0.10m);
        _stopLossPct     = config.GetValue<decimal>("BettingSettings:StopLossPercent", 0.20m);
        _maxExposurePct  = config.GetValue<decimal>("BettingSettings:MaxExposurePercent", 0.10m);
    }

    public async Task<Bankroll> GetBankrollAsync(int userId)
    {
        var state = await GetStateAsync(userId);
        await ResetDailyIfNeededAsync(userId, state);
        lock (state.Lock) { return BuildSnapshot(state); }
    }

    public async Task ReserveStakeAsync(int userId, decimal stake)
    {
        var state = await GetStateAsync(userId);
        lock (state.Lock) { state.AvailableBankroll -= stake; }
        await SaveSnapshotAsync(userId, state);
    }

    public async Task UpdateAfterResultAsync(int userId, decimal stake, decimal odds, string result)
    {
        var state = await GetStateAsync(userId);
        lock (state.Lock)
        {
            if (result == "Win")
            {
                decimal profit          = stake * (odds - 1m);
                state.AvailableBankroll += stake + profit;
                state.TotalBankroll     += profit;
            }
            else
            {
                state.DailyLossUsed  += stake;
                state.CumulativeLoss += stake;
                state.TotalBankroll  -= stake;
            }
        }
        await SaveSnapshotAsync(userId, state);
    }

    public async Task ResetAsync(int userId, decimal? newAmount = null)
    {
        var state = await GetStateAsync(userId);
        lock (state.Lock)
        {
            var amount               = newAmount ?? _initialBankroll;
            state.TotalBankroll      = amount;
            state.AvailableBankroll  = amount;
            state.DailyLossUsed      = 0;
            state.CumulativeLoss     = 0;
            state.StartOfDayBankroll = amount;
            state.LastResetDate      = DateTime.UtcNow.Date;
        }
        await SaveSnapshotAsync(userId, state);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private sealed class BankrollState
    {
        public decimal  TotalBankroll;
        public decimal  AvailableBankroll;
        public decimal  DailyLossUsed;
        public decimal  CumulativeLoss;
        /// <summary>Bankroll at the moment today's tracking window began — the fixed
        /// reference for DailyLossLimit, so the limit doesn't self-tighten as losses
        /// accrue during the day (see BuildSnapshot).</summary>
        public decimal  StartOfDayBankroll;
        public DateTime LastResetDate;
        public readonly object Lock = new();
    }

    private Bankroll BuildSnapshot(BankrollState s) => new()
    {
        TotalBankroll     = s.TotalBankroll,
        AvailableBankroll = s.AvailableBankroll,
        MaxStakePerBet    = s.TotalBankroll * _maxStakePct,
        // Fixed to the bankroll at the start of *today*, not the live, shrinking
        // TotalBankroll — otherwise "10% daily loss limit" tightens itself further
        // with every loss instead of staying 10% of what the day started with.
        // StopLossLimit uses the same fixed-reference convention, pinned to the
        // account's initial bankroll instead of the current one.
        DailyLossLimit    = s.StartOfDayBankroll * _dailyLossPct,
        StopLossLimit     = _initialBankroll * _stopLossPct,
        // MaxExposure is intentionally different: exposure is a live, point-in-time
        // concept (how much of your *current* capital is at risk right now), not a
        // cumulative-drawdown concept, so it correctly tracks current TotalBankroll.
        MaxExposure       = s.TotalBankroll * _maxExposurePct,
        DailyLossUsed     = s.DailyLossUsed,
        CumulativeLoss    = s.CumulativeLoss,
        LastResetDate     = s.LastResetDate,
    };

    private async Task ResetDailyIfNeededAsync(int userId, BankrollState state)
    {
        bool needsReset;
        lock (state.Lock)
        {
            needsReset = state.LastResetDate < DateTime.UtcNow.Date;
            if (needsReset)
            {
                state.DailyLossUsed      = 0;
                state.StartOfDayBankroll = state.TotalBankroll;
                state.LastResetDate      = DateTime.UtcNow.Date;
            }
        }
        if (needsReset) await SaveSnapshotAsync(userId, state);
    }

    /// <summary>Returns the in-memory state for a user, loading it from the latest DB snapshot on first access.</summary>
    private async Task<BankrollState> GetStateAsync(int userId)
    {
        if (_states.TryGetValue(userId, out var existing)) return existing;

        await _loadLock.WaitAsync();
        try
        {
            if (_states.TryGetValue(userId, out existing)) return existing;

            var state = await LoadStateAsync(userId);
            _states[userId] = state;
            return state;
        }
        finally { _loadLock.Release(); }
    }

    private async Task<BankrollState> LoadStateAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo             = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();
        var latest            = await repo.GetLatestSnapshotAsync(userId);

        if (latest is not null)
        {
            var isFromToday = latest.SnapshotDate.Date == DateTime.UtcNow.Date;
            return new BankrollState
            {
                TotalBankroll     = latest.TotalBankroll,
                AvailableBankroll = latest.AvailableBankroll,
                DailyLossUsed     = latest.DailyLossUsed,
                CumulativeLoss    = latest.CumulativeLoss,
                // Best-effort reconstruction on cold restart mid-day: the true
                // start-of-day value isn't persisted, so approximate it by adding
                // today's realized losses back onto the current total. This
                // overestimates if there were also wins today (no running total of
                // today's win profit is persisted) — errs toward a more generous,
                // not more restrictive, limit, which is the safer direction to be
                // wrong in. If the snapshot is from a prior day, its TotalBankroll
                // *is* today's starting point (no bets placed yet today).
                StartOfDayBankroll = isFromToday
                    ? latest.TotalBankroll + latest.DailyLossUsed
                    : latest.TotalBankroll,
                LastResetDate     = latest.SnapshotDate.Date,
            };
        }

        var state = new BankrollState
        {
            TotalBankroll      = _initialBankroll,
            AvailableBankroll  = _initialBankroll,
            DailyLossUsed      = 0,
            CumulativeLoss     = 0,
            StartOfDayBankroll = _initialBankroll,
            LastResetDate      = DateTime.UtcNow.Date,
        };
        await SaveSnapshotAsync(userId, state);
        return state;
    }

    private async Task SaveSnapshotAsync(int userId, BankrollState state)
    {
        decimal total, available, dailyLoss, cumLoss;
        lock (state.Lock)
        {
            total     = state.TotalBankroll;
            available = state.AvailableBankroll;
            dailyLoss = state.DailyLossUsed;
            cumLoss   = state.CumulativeLoss;
        }

        await using var scope   = _scopeFactory.CreateAsyncScope();
        var repo                = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();
        var betRepo             = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        // Aggregate queries — no full table fetch
        var (betsTotal, wins, losses, pnl, avgCLV) = await betRepo.GetSettledStatsAsync(userId);
        var exposure     = await betRepo.GetTotalExposureAsync(userId);
        var consecLosses = await betRepo.GetConsecutiveLossesAsync(userId);

        await repo.AddAsync(new BankrollSnapshot
        {
            UserId            = userId,
            TotalBankroll     = total,
            AvailableBankroll = available,
            TotalExposure     = exposure,
            DailyLossUsed     = dailyLoss,
            CumulativeLoss    = cumLoss,
            ConsecutiveLosses = consecLosses,
            TotalPnL          = pnl,
            ROI               = _initialBankroll > 0 ? (double)(pnl / _initialBankroll) : 0,
            TotalBetsPlaced   = betsTotal,
            WinCount          = wins,
            LossCount         = losses,
            WinRate           = betsTotal > 0 ? (double)wins / betsTotal : 0,
            AverageCLV        = avgCLV,
            SnapshotDate      = DateTime.UtcNow
        });
    }
}
