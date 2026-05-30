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

    private decimal  _totalBankroll;
    private decimal  _availableBankroll;
    private decimal  _dailyLossUsed;
    private decimal  _cumulativeLoss;
    private DateTime _lastResetDate;

    private readonly object _lock = new();
    private const int DefaultUserId = 1;

    public BankrollService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory    = scopeFactory;
        _initialBankroll = config.GetValue<decimal>("BettingSettings:InitialBankroll", 10_000m);
        _maxStakePct     = config.GetValue<decimal>("BettingSettings:MaxStakePercent", 0.03m);
        _dailyLossPct    = config.GetValue<decimal>("BettingSettings:DailyLossLimitPercent", 0.10m);
        _stopLossPct     = config.GetValue<decimal>("BettingSettings:StopLossPercent", 0.20m);
        _maxExposurePct  = config.GetValue<decimal>("BettingSettings:MaxExposurePercent", 0.10m);

        // Blocking only here at startup — no request context exists yet, so no deadlock risk.
        LoadLatestSnapshot();
    }

    public async Task<Bankroll> GetBankrollAsync()
    {
        await ResetDailyIfNeededAsync();
        lock (_lock)
        {
            return BuildSnapshot();
        }
    }

    public async Task ReserveStakeAsync(decimal stake)
    {
        lock (_lock) { _availableBankroll -= stake; }
        await SaveSnapshotAsync();
    }

    public async Task UpdateAfterResultAsync(decimal stake, decimal odds, string result)
    {
        lock (_lock)
        {
            if (result == "Win")
            {
                decimal profit     = stake * (odds - 1m);
                _availableBankroll += stake + profit;
                _totalBankroll     += profit;
            }
            else
            {
                _dailyLossUsed  += stake;
                _cumulativeLoss += stake;
                _totalBankroll  -= stake;
            }
        }
        await SaveSnapshotAsync();
    }

    public async Task ResetAsync(decimal? newAmount = null)
    {
        lock (_lock)
        {
            var amount         = newAmount ?? _initialBankroll;
            _totalBankroll     = amount;
            _availableBankroll = amount;
            _dailyLossUsed     = 0;
            _cumulativeLoss    = 0;
            _lastResetDate     = DateTime.UtcNow.Date;
        }
        await SaveSnapshotAsync();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private Bankroll BuildSnapshot() => new()
    {
        TotalBankroll     = _totalBankroll,
        AvailableBankroll = _availableBankroll,
        MaxStakePerBet    = _totalBankroll * _maxStakePct,
        DailyLossLimit    = _totalBankroll * _dailyLossPct,
        StopLossLimit     = _initialBankroll * _stopLossPct,
        MaxExposure       = _totalBankroll * _maxExposurePct,
        DailyLossUsed     = _dailyLossUsed,
        CumulativeLoss    = _cumulativeLoss,
        LastResetDate     = _lastResetDate,
    };

    private async Task ResetDailyIfNeededAsync()
    {
        bool needsReset;
        lock (_lock)
        {
            needsReset = _lastResetDate < DateTime.UtcNow.Date;
            if (needsReset) { _dailyLossUsed = 0; _lastResetDate = DateTime.UtcNow.Date; }
        }
        if (needsReset) await SaveSnapshotAsync();
    }

    private void LoadLatestSnapshot()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo        = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();

        var latest = repo.GetLatestSnapshotAsync(DefaultUserId).GetAwaiter().GetResult();

        if (latest is not null)
        {
            _totalBankroll     = latest.TotalBankroll;
            _availableBankroll = latest.AvailableBankroll;
            _dailyLossUsed     = latest.DailyLossUsed;
            _cumulativeLoss    = latest.CumulativeLoss;
            _lastResetDate     = latest.SnapshotDate.Date;
        }
        else
        {
            _totalBankroll     = _initialBankroll;
            _availableBankroll = _initialBankroll;
            _dailyLossUsed     = 0;
            _cumulativeLoss    = 0;
            _lastResetDate     = DateTime.UtcNow.Date;
            SaveSnapshotAsync().GetAwaiter().GetResult(); // Only blocks once at first startup
        }
    }

    private async Task SaveSnapshotAsync()
    {
        decimal total, available, dailyLoss, cumLoss;
        lock (_lock)
        {
            total     = _totalBankroll;
            available = _availableBankroll;
            dailyLoss = _dailyLossUsed;
            cumLoss   = _cumulativeLoss;
        }

        await using var scope   = _scopeFactory.CreateAsyncScope();
        var repo                = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();
        var betRepo             = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        // Aggregate queries — no full table fetch
        var (betsTotal, wins, losses, pnl, avgCLV) = await betRepo.GetSettledStatsAsync(DefaultUserId);
        var exposure     = await betRepo.GetTotalExposureAsync(DefaultUserId);
        var consecLosses = await betRepo.GetConsecutiveLossesAsync(DefaultUserId);

        await repo.AddAsync(new BankrollSnapshot
        {
            UserId            = DefaultUserId,
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
