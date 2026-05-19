namespace BettingAnalysis.Models;

/// <summary>
/// Live-editable configuration for all betting rules.
/// Updated via PUT /Betting/settings — changes apply immediately without restart.
/// Initial values are seeded from appsettings.json by BettingConfigService.
/// </summary>
public class BettingConfig
{
    // ── Edge rules ────────────────────────────────────────────────────────────
    /// <summary>Rule #2: Minimum edge to show/place a bet (default 5%).</summary>
    public double EdgeThreshold { get; set; } = 0.05;

    /// <summary>Rule #3: Edge above this triggers manual verification warning (default 20%).</summary>
    public double HighEdgeThreshold { get; set; } = 0.20;

    // ── Sizing ────────────────────────────────────────────────────────────────
    /// <summary>Fractional Kelly multiplier. 0.5 = half-Kelly (recommended).</summary>
    public double KellyFraction { get; set; } = 0.5;

    /// <summary>Rule: Hard cap on stake as fraction of bankroll (default 3%).</summary>
    public double MaxStakePercent { get; set; } = 0.03;

    // ── Bankroll limits ───────────────────────────────────────────────────────
    /// <summary>Rule: Stop betting for the day if daily loss exceeds this fraction (default 10%).</summary>
    public double DailyLossLimitPercent { get; set; } = 0.10;

    /// <summary>Rule: Halt system entirely if cumulative drawdown exceeds this fraction (default 20%).</summary>
    public double StopLossPercent { get; set; } = 0.20;

    /// <summary>Rule: Total open bet exposure must not exceed this fraction of bankroll (default 10%).</summary>
    public double MaxExposurePercent { get; set; } = 0.10;

    // ── Timing window ─────────────────────────────────────────────────────────
    /// <summary>Rule: Do not bet less than this many hours before kickoff (default 1h).</summary>
    public double PreMatchMinHours { get; set; } = 1.0;

    /// <summary>Rule: Do not bet more than this many hours before kickoff (default 336h = 2 weeks).</summary>
    public double PreMatchMaxHours { get; set; } = 336.0;

    // ── Tilt protection ───────────────────────────────────────────────────────
    /// <summary>Rule: Halt betting after this many consecutive losses (default 3).</summary>
    public int MaxConsecutiveLosses { get; set; } = 3;

    // ── Correlation / exposure per match ──────────────────────────────────────
    /// <summary>Rule: Max simultaneous bets on the same match (default 2).</summary>
    public int MaxBetsPerMatch { get; set; } = 2;

    // ── Emotional bias protection ─────────────────────────────────────────────
    /// <summary>Rule: Teams on this list are never bet on regardless of edge.</summary>
    public List<string> TeamBlacklist { get; set; } = new();

    // ── Line movement ─────────────────────────────────────────────────────────
    /// <summary>Rule: Block bets when odds are drifting against prediction.</summary>
    public bool RequireLineMovementCheck { get; set; } = true;
}
