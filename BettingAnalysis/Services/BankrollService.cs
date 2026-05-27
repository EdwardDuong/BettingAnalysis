using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Manages the core bankroll state with database persistence.
/// Automatically creates snapshots after each result update for historical tracking.
/// Loads latest snapshot on startup for state recovery.
/// </summary>
public class BankrollService : IBankrollService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly decimal _maxStakePct;
    private readonly decimal _dailyLossPct;
    private readonly decimal _stopLossPct;
    private readonly decimal _maxExposurePct;
    private readonly decimal _initialBankroll;

    private decimal _totalBankroll;
    private decimal _availableBankroll;
    private decimal _dailyLossUsed;
    private decimal _cumulativeLoss;
    private DateTime _lastResetDate;

    private readonly object _lock = new();
    private const int DefaultUserId = 1; // Individual use — default user

    public BankrollService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;

        _initialBankroll   = config.GetValue<decimal>("BettingSettings:InitialBankroll", 10_000m);
        _maxStakePct       = config.GetValue<decimal>("BettingSettings:MaxStakePercent", 0.03m);
        _dailyLossPct      = config.GetValue<decimal>("BettingSettings:DailyLossLimitPercent", 0.10m);
        _stopLossPct       = config.GetValue<decimal>("BettingSettings:StopLossPercent", 0.20m);
        _maxExposurePct    = config.GetValue<decimal>("BettingSettings:MaxExposurePercent", 0.10m);

        LoadLatestSnapshot();
    }

    /// <summary>Returns a snapshot of core bankroll state (without exposure/tilt — added by controller).</summary>
    public Bankroll GetBankroll()
    {
        lock (_lock)
        {
            ResetDailyIfNeeded();
            return new Bankroll
            {
                TotalBankroll     = _totalBankroll,
                AvailableBankroll = _availableBankroll,
                MaxStakePerBet    = _totalBankroll * _maxStakePct,
                DailyLossLimit    = _totalBankroll * _dailyLossPct,
                StopLossLimit     = _initialBankroll * _stopLossPct,   // Fixed to initial for cumulative measure
                MaxExposure       = _totalBankroll * _maxExposurePct,
                DailyLossUsed     = _dailyLossUsed,
                CumulativeLoss    = _cumulativeLoss,
                LastResetDate     = _lastResetDate,
            };
        }
    }

    public void ReserveStake(decimal stake)
    {
        lock (_lock)
        {
            _availableBankroll -= stake;
            SaveSnapshot(); // Persist state change
        }
    }

    /// <summary>Rule #10: Update bankroll after every result.</summary>
    public void UpdateAfterResult(decimal stake, decimal odds, string result)
    {
        lock (_lock)
        {
            if (result == "Win")
            {
                decimal profit = stake * (odds - 1m);
                _availableBankroll += stake + profit;
                _totalBankroll     += profit;
            }
            else
            {
                _dailyLossUsed  += stake;
                _cumulativeLoss += stake;
                _totalBankroll  -= stake;
            }

            SaveSnapshot(); // Persist after every result
        }
    }

    /// <summary>
    /// Hard reset — wipes all loss counters and restores the bankroll to
    /// <paramref name="newAmount"/> (defaults to the configured initial bankroll).
    /// </summary>
    public void Reset(decimal? newAmount = null)
    {
        lock (_lock)
        {
            var amount         = newAmount ?? _initialBankroll;
            _totalBankroll     = amount;
            _availableBankroll = amount;
            _dailyLossUsed     = 0;
            _cumulativeLoss    = 0;
            _lastResetDate     = DateTime.UtcNow.Date;

            SaveSnapshot(); // Persist reset
        }
    }

    private void ResetDailyIfNeeded()
    {
        var today = DateTime.UtcNow.Date;
        if (_lastResetDate < today)
        {
            _dailyLossUsed = 0;
            _lastResetDate = today;
            SaveSnapshot(); // Persist daily reset
        }
    }

    // ── Database persistence ──────────────────────────────────────────────────

    private void LoadLatestSnapshot()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();
        var betRepo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var latest = repo.GetLatestSnapshotAsync(DefaultUserId).GetAwaiter().GetResult();

        if (latest != null)
        {
            _totalBankroll     = latest.TotalBankroll;
            _availableBankroll = latest.AvailableBankroll;
            _dailyLossUsed     = latest.DailyLossUsed;
            _cumulativeLoss    = latest.CumulativeLoss;
            _lastResetDate     = latest.SnapshotDate.Date;
        }
        else
        {
            // First run — initialize with config values
            _totalBankroll     = _initialBankroll;
            _availableBankroll = _initialBankroll;
            _dailyLossUsed     = 0;
            _cumulativeLoss    = 0;
            _lastResetDate     = DateTime.UtcNow.Date;

            SaveSnapshot(); // Create initial snapshot
        }
    }

    private void SaveSnapshot()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankrollSnapshotRepository>();
        var betRepo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        // Calculate performance metrics
        var allBets = betRepo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult().ToList();
        var settledBets = allBets.Where(b => b.Result != "Pending").ToList();

        int totalBets = settledBets.Count;
        int wins = settledBets.Count(b => b.Result == "Win");
        int losses = settledBets.Count(b => b.Result == "Loss");
        double winRate = totalBets > 0 ? (double)wins / totalBets : 0;
        decimal totalPnL = settledBets.Sum(b => b.PnL);
        double roi = _initialBankroll > 0 ? (double)(totalPnL / _initialBankroll) : 0;
        double avgCLV = settledBets.Any(b => b.CLV.HasValue)
            ? settledBets.Where(b => b.CLV.HasValue).Average(b => b.CLV!.Value)
            : 0;

        var snapshot = new BankrollSnapshot
        {
            UserId = DefaultUserId,
            TotalBankroll = _totalBankroll,
            AvailableBankroll = _availableBankroll,
            TotalExposure = betRepo.GetTotalExposureAsync(DefaultUserId).GetAwaiter().GetResult(),
            DailyLossUsed = _dailyLossUsed,
            CumulativeLoss = _cumulativeLoss,
            ConsecutiveLosses = betRepo.GetConsecutiveLossesAsync(DefaultUserId).GetAwaiter().GetResult(),
            TotalPnL = totalPnL,
            ROI = roi,
            TotalBetsPlaced = totalBets,
            WinCount = wins,
            LossCount = losses,
            WinRate = winRate,
            AverageCLV = avgCLV,
            SnapshotDate = DateTime.UtcNow
        };

        repo.AddAsync(snapshot).GetAwaiter().GetResult();
    }
}
