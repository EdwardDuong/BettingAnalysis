namespace BettingAnalysis.Models;

/// <summary>
/// Complete risk snapshot of the bankroll at a point in time.
/// Combines BankrollService (money state) with BettingLoggingService (exposure, tilt).
/// </summary>
public class Bankroll
{
    // ── Core bankroll ─────────────────────────────────────────────────────────
    public decimal TotalBankroll { get; set; }
    public decimal AvailableBankroll { get; set; }

    // ── Limits ────────────────────────────────────────────────────────────────
    public decimal MaxStakePerBet { get; set; }
    public decimal DailyLossLimit { get; set; }
    public decimal StopLossLimit { get; set; }

    // ── Current usage ─────────────────────────────────────────────────────────
    public decimal DailyLossUsed { get; set; }
    public decimal CumulativeLoss { get; set; }

    /// <summary>Sum of stakes on all Pending bets. Must stay ≤ MaxExposure.</summary>
    public decimal TotalExposure { get; set; }
    public decimal MaxExposure { get; set; }

    // ── Tilt protection ───────────────────────────────────────────────────────
    public int ConsecutiveLosses { get; set; }
    public int MaxConsecutiveLosses { get; set; }

    // ── Status flags ──────────────────────────────────────────────────────────
    public bool IsDailyLimitReached => DailyLossUsed >= DailyLossLimit;
    public bool IsStopLossTriggered => CumulativeLoss >= StopLossLimit;
    public bool IsTiltProtectionActive => ConsecutiveLosses >= MaxConsecutiveLosses;
    public bool IsExposureLimitReached => TotalExposure >= MaxExposure;

    public DateTime LastResetDate { get; set; } = DateTime.UtcNow.Date;
}
