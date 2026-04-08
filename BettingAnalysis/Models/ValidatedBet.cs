namespace BettingAnalysis.Models;

/// <summary>
/// AI Validator output for a single betting opportunity.
/// Decision + Score + Flags give the frontend everything needed to
/// colour-code, sort, and warn the user intelligently.
/// </summary>
public class ValidatedBet
{
    public string MatchId  { get; set; } = string.Empty;
    public string Team     { get; set; } = string.Empty;
    public string Outcome  { get; set; } = string.Empty;

    /// <summary>"GOOD_BET" | "RISKY" | "SKIP"</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>Composite quality score 0–10. Higher = better bet.</summary>
    public int Score { get; set; }

    /// <summary>Active flags explaining the decision.</summary>
    public List<string> Flags { get; set; } = new();

    /// <summary>Human-readable explanation of the decision.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// All possible AI Validator flags.
/// Major flags (heavy penalty): HIGH_EDGE, LINE_MOVING_AGAINST
/// Minor flags (light penalty): ODDS_TOO_LOW, HIGH_VARIANCE, CORRELATED_BET, BAD_TIMING, EPL_LOW_EDGE
/// </summary>
public static class ValidationFlags
{
    // ── Major flags (−2 score each) ───────────────────────────────────────────
    /// <summary>Edge > 15% — likely a false edge (stale odds, missing news).</summary>
    public const string HighEdge           = "HIGH_EDGE";

    /// <summary>Market odds have drifted — sharp money is on the other side.</summary>
    public const string LineMovingAgainst  = "LINE_MOVING_AGAINST";

    // ── Minor flags (−1 score each) ───────────────────────────────────────────
    /// <summary>Odds < 1.5 — very low payout, tiny margin for error.</summary>
    public const string OddsTooLow        = "ODDS_TOO_LOW";

    /// <summary>Odds > 3.0 — high variance, bankroll-unfriendly.</summary>
    public const string HighVariance      = "HIGH_VARIANCE";

    /// <summary>Multiple bets on the same match — correlated outcome risk.</summary>
    public const string CorrelatedBet     = "CORRELATED_BET";

    /// <summary>Match outside the 1–6h betting window.</summary>
    public const string BadTiming         = "BAD_TIMING";

    /// <summary>EPL market with edge < 8% — market too efficient for thin edge.</summary>
    public const string EplLowEdge        = "EPL_LOW_EDGE";

    // ── Positive signals (displayed as green tags, no penalty) ────────────────
    /// <summary>Odds shortening — market agrees with prediction.</summary>
    public const string Steaming          = "STEAMING";
}
