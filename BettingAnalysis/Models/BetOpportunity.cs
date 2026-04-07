namespace BettingAnalysis.Models;

/// <summary>
/// A value bet opportunity passing all pre-flight checks.
/// Returned by GET /Betting/opportunities.
///
/// Flags guide the frontend on how to display and warn the user.
/// ValidationWarnings are soft alerts shown in the UI without blocking the bet.
/// </summary>
public class BetOpportunity
{
    public string MatchId { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public decimal Odds { get; set; }
    public double Probability { get; set; }
    public double Edge { get; set; }
    public decimal SuggestedStake { get; set; }
    public SportType SportType { get; set; }
    public DateTime MatchStartTime { get; set; }
    public double HoursUntilKickoff { get; set; }

    // ── Line movement ─────────────────────────────────────────────────────────
    /// <summary>"Stable" | "Steaming" | "Drifting"</summary>
    public string LineMovementStatus { get; set; } = "Stable";

    /// <summary>Previous odds for this outcome (for display).</summary>
    public decimal? PreviousOdds { get; set; }

    // ── Risk flags ────────────────────────────────────────────────────────────
    /// <summary>Edge > 20% OR line drifting — treat with extra caution.</summary>
    public bool IsHighRisk { get; set; }

    /// <summary>Edge > 20% — model inputs need manual double-check before placing.</summary>
    public bool RequiresManualCheck { get; set; }

    /// <summary>Soft warnings from validation (doesn't block the bet).</summary>
    public List<string> ValidationWarnings { get; set; } = new();
}
