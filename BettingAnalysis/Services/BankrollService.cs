using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Manages the core bankroll state (money in/out, daily counters).
/// Exposure and consecutive-losses stats live in BettingLoggingService
/// and are merged into the Bankroll snapshot by the controller.
/// </summary>
public class BankrollService
{
    private readonly decimal _maxStakePct;
    private readonly decimal _dailyLossPct;
    private readonly decimal _stopLossPct;
    private readonly decimal _maxExposurePct;

    private decimal _totalBankroll;
    private decimal _availableBankroll;
    private decimal _dailyLossUsed;
    private decimal _cumulativeLoss;
    private DateTime _lastResetDate;

    private readonly object _lock = new();

    public BankrollService(IConfiguration config)
    {
        var initial        = config.GetValue<decimal>("BettingSettings:InitialBankroll", 10_000m);
        _maxStakePct       = config.GetValue<decimal>("BettingSettings:MaxStakePercent", 0.03m);
        _dailyLossPct      = config.GetValue<decimal>("BettingSettings:DailyLossLimitPercent", 0.10m);
        _stopLossPct       = config.GetValue<decimal>("BettingSettings:StopLossPercent", 0.20m);
        _maxExposurePct    = config.GetValue<decimal>("BettingSettings:MaxExposurePercent", 0.10m);

        _totalBankroll     = initial;
        _availableBankroll = initial;
        _dailyLossUsed     = 0;
        _cumulativeLoss    = 0;
        _lastResetDate     = DateTime.UtcNow.Date;
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
                StopLossLimit     = _totalBankroll * _stopLossPct,   // Fixed to original for cumulative measure
                MaxExposure       = _totalBankroll * _maxExposurePct,
                DailyLossUsed     = _dailyLossUsed,
                CumulativeLoss    = _cumulativeLoss,
                LastResetDate     = _lastResetDate,
            };
        }
    }

    public void ReserveStake(decimal stake)
    {
        lock (_lock) { _availableBankroll -= stake; }
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
                // Limits scale with bankroll so they don't become trivially easy to breach
            }
        }
    }

    private void ResetDailyIfNeeded()
    {
        var today = DateTime.UtcNow.Date;
        if (_lastResetDate < today) { _dailyLossUsed = 0; _lastResetDate = today; }
    }
}
