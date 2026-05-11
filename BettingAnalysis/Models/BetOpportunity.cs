namespace BettingAnalysis.Models;

/// <summary>
/// A value bet opportunity enriched with AI Validator output.
/// Only opportunities passing the edge threshold and timing window reach this stage.
/// </summary>
public class BetOpportunity
{
    public string MatchId   { get; set; } = string.Empty;
    public string HomeTeam  { get; set; } = string.Empty;
    public string AwayTeam  { get; set; } = string.Empty;
    public string Team      { get; set; } = string.Empty;
    public string Outcome   { get; set; } = string.Empty;
    public decimal Odds     { get; set; }
    public double Probability   { get; set; }
    public double Edge          { get; set; }
    public decimal SuggestedStake { get; set; }
    public SportType SportType  { get; set; }
    public DateTime MatchStartTime { get; set; }
    public double HoursUntilKickoff { get; set; }

    // ── Line movement ─────────────────────────────────────────────────────────
    /// <summary>"Stable" | "Steaming" | "Drifting"</summary>
    public string LineMovementStatus { get; set; } = "Stable";
    public decimal? PreviousOdds { get; set; }

    // ── Risk flags (from ValidationService) ──────────────────────────────────
    /// <summary>Edge > 20% OR line drifting — treat with extra caution.</summary>
    public bool IsHighRisk { get; set; }

    /// <summary>Edge > 20% — model inputs need manual double-check.</summary>
    public bool RequiresManualCheck { get; set; }

    /// <summary>Soft warnings that don't block the bet.</summary>
    public List<string> ValidationWarnings { get; set; } = new();

    // ── Confidence ────────────────────────────────────────────────────────────
    /// <summary>"High" | "Medium" | "Low" — derived from edge and model probability.</summary>
    public string ConfidenceLevel { get; set; } = "Low";

    // ── AI Validator output ───────────────────────────────────────────────────
    /// <summary>
    /// Scoring, decision, and flags from AIValidatorService.
    /// Decision: "GOOD_BET" | "RISKY" | "SKIP"
    /// Score: 0–10 (used for sorting — higher = better)
    /// </summary>
    public ValidatedBet? AiValidation { get; set; }
}
