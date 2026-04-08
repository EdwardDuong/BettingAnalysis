using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// AI Validation Layer — scores and classifies betting opportunities.
///
/// This service does NOT predict outcomes. It validates opportunities produced by
/// the Poisson model by applying a second, independent rule set focused purely
/// on risk control and market efficiency signals.
///
/// Philosophy:
///   → Reduce bad bets, not maximise bet count
///   → Flag uncertainty rather than guess through it
///   → Reward consistency, penalise variance
///
/// Scoring (0–10):
///   Base score: 5
///   Edge bonuses: +1 if edge > 7%, +2 if edge > 10%
///   Major penalties (−2): HIGH_EDGE, LINE_MOVING_AGAINST
///   Minor penalties (−1): ODDS_TOO_LOW, HIGH_VARIANCE, CORRELATED_BET, BAD_TIMING, EPL_LOW_EDGE
///   Clamped to [0, 10]
///
/// Decision:
///   GOOD_BET → score ≥ 6, edge ≥ 6%, no major flags
///   RISKY    → 1–2 flags OR score 3–5
///   SKIP     → edge < 5%, score ≤ 2, or 3+ flags
/// </summary>
public class AIValidatorService
{
    private readonly BettingConfigService  _cfg;

    // Thresholds (also configurable via BettingConfig)
    private const double EdgeSkipBelow      = 0.05;
    private const double EdgeGoodAbove      = 0.06;
    private const double EdgeBonusLow       = 0.07;
    private const double EdgeBonusHigh      = 0.10;
    private const double EdgeHighFlag       = 0.15;
    private const double OddsLowThreshold   = 1.50;
    private const double OddsHighThreshold  = 3.00;
    private const double EplEfficiencyEdge  = 0.08;

    public AIValidatorService(BettingConfigService cfg) => _cfg = cfg;

    /// <summary>
    /// Validate a list of opportunities. Operates on the full list so it can
    /// detect correlation (multiple bets on the same match).
    /// </summary>
    public List<ValidatedBet> Validate(List<BetOpportunity> opportunities)
    {
        // Pre-compute match groups for correlation detection (Rule 5)
        var perMatch = opportunities
            .GroupBy(o => o.MatchId)
            .ToDictionary(g => g.Key, g => g.Count());

        return opportunities
            .Select(opp => ValidateOne(opp, perMatch))
            .ToList();
    }

    private ValidatedBet ValidateOne(BetOpportunity opp, Dictionary<string, int> perMatch)
    {
        var flags = new List<string>();
        int score = 5;

        // ── Rule 1: Edge rules ────────────────────────────────────────────────
        if (opp.Edge > EdgeHighFlag)
        {
            // High edge often signals stale odds, missing team news, or model error
            flags.Add(ValidationFlags.HighEdge);
            score -= 2;
        }

        // ── Rule 2: Odds rules ────────────────────────────────────────────────
        if ((double)opp.Odds < OddsLowThreshold)
        {
            // At odds < 1.5, you need an 80%+ strike rate — very punishing on misses
            flags.Add(ValidationFlags.OddsTooLow);
            score -= 1;
        }

        if ((double)opp.Odds > OddsHighThreshold)
        {
            // High odds = high variance = Kelly recommends small stakes anyway
            flags.Add(ValidationFlags.HighVariance);
            score -= 1;
        }

        // ── Rule 3: Line movement ─────────────────────────────────────────────
        if (opp.LineMovementStatus == "Drifting")
        {
            // Market is moving away from this selection — sharp money disagrees
            flags.Add(ValidationFlags.LineMovingAgainst);
            score -= 2;
        }
        else if (opp.LineMovementStatus == "Steaming")
        {
            // Positive signal — market agrees. No penalty, add tag for display
            flags.Add(ValidationFlags.Steaming);
            // Steaming is a positive signal, slight score boost applied via edge bonus below
        }

        // ── Rule 4: Timing window ─────────────────────────────────────────────
        // OddsService already filters by window, but check again for defence-in-depth
        var config = _cfg.Get();
        var hoursUntil = opp.HoursUntilKickoff;
        if (hoursUntil < config.PreMatchMinHours || hoursUntil > config.PreMatchMaxHours)
        {
            flags.Add(ValidationFlags.BadTiming);
            score -= 1;
        }

        // ── Rule 5: Correlation ────────────────────────────────────────────────
        if (perMatch.TryGetValue(opp.MatchId, out int count) && count > 1)
        {
            // Multiple outcomes on the same match are partially correlated
            flags.Add(ValidationFlags.CorrelatedBet);
            score -= 1;
        }

        // ── Rule 6: Market efficiency (EPL) ───────────────────────────────────
        if (opp.SportType == SportType.EPL && opp.Edge < EplEfficiencyEdge)
        {
            // EPL is the most efficiently priced football league.
            // Edge < 8% in EPL is not convincing enough — markets are too tight.
            flags.Add(ValidationFlags.EplLowEdge);
            score -= 1;
        }

        // ── Edge bonuses ──────────────────────────────────────────────────────
        if (opp.Edge > EdgeBonusHigh)      score += 2;
        else if (opp.Edge > EdgeBonusLow)  score += 1;

        // Clamp score to [0, 10]
        score = Math.Clamp(score, 0, 10);

        // ── Decision ──────────────────────────────────────────────────────────
        var majorFlags = flags.Where(f => f is ValidationFlags.HighEdge or ValidationFlags.LineMovingAgainst).ToList();
        var minorFlags = flags.Except(majorFlags).Where(f => f != ValidationFlags.Steaming).ToList();
        var totalRiskFlags = majorFlags.Count + minorFlags.Count;

        string decision;
        if (opp.Edge < EdgeSkipBelow || totalRiskFlags >= 3 || score <= 2)
            decision = "SKIP";
        else if (majorFlags.Count >= 1 || minorFlags.Count >= 2 || score <= 5)
            decision = "RISKY";
        else if (opp.Edge >= EdgeGoodAbove && score >= 6)
            decision = "GOOD_BET";
        else
            decision = "RISKY";

        return new ValidatedBet
        {
            MatchId  = opp.MatchId,
            Team     = opp.Team,
            Outcome  = opp.Outcome,
            Decision = decision,
            Score    = score,
            Flags    = flags,
            Reason   = BuildReason(opp, decision, score, flags, majorFlags, minorFlags)
        };
    }

    private static string BuildReason(
        BetOpportunity opp, string decision, int score,
        List<string> allFlags, List<string> majorFlags, List<string> minorFlags)
    {
        var riskFlags = allFlags.Where(f => f != ValidationFlags.Steaming).ToList();

        if (decision == "GOOD_BET")
        {
            var steamNote = allFlags.Contains(ValidationFlags.Steaming) ? " Market steaming in our favour." : "";
            return $"Edge {opp.Edge:P1} at odds {opp.Odds:F2} — solid value, no major concerns. Score {score}/10.{steamNote}";
        }

        if (decision == "SKIP")
        {
            if (opp.Edge < EdgeSkipBelow)
                return $"Edge {opp.Edge:P1} is below the 5% minimum. No value.";
            if (majorFlags.Any())
                return $"Blocked by: {string.Join(", ", majorFlags)}. Score {score}/10. Too risky.";
            return $"{riskFlags.Count} risk flags accumulated — risk/reward unfavourable. Score {score}/10.";
        }

        // RISKY
        if (majorFlags.Any())
            return $"Major concern: {string.Join(", ", majorFlags)}. Reduce stake or skip. Score {score}/10.";

        return $"Minor flags: {string.Join(", ", minorFlags)}. Proceed with reduced stake. Score {score}/10.";
    }
}
