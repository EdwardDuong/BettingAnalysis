namespace BettingAnalysis.Data.Entities;

/// <summary>
/// Database entity for tracking bankroll state over time.
/// Enables performance analysis, drawdown tracking, and ROI calculation.
/// </summary>
public class BankrollSnapshot
{
    public int Id { get; set; }
    public int UserId { get; set; }

    // ── Core state ────────────────────────────────────────────────────────────
    public decimal TotalBankroll { get; set; }
    public decimal AvailableBankroll { get; set; }
    public decimal TotalExposure { get; set; }

    // ── Risk metrics ──────────────────────────────────────────────────────────
    public decimal DailyLossUsed { get; set; }
    public decimal CumulativeLoss { get; set; }
    public int ConsecutiveLosses { get; set; }

    // ── Performance metrics ───────────────────────────────────────────────────
    public decimal TotalPnL { get; set; }
    public double ROI { get; set; }
    public int TotalBetsPlaced { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public double WinRate { get; set; }
    public double AverageCLV { get; set; }

    // ── Timestamp ─────────────────────────────────────────────────────────────
    public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ─────────────────────────────────────────────────
    public virtual User User { get; set; } = null!;
}
