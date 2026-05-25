using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// AI Validation Layer — scores and classifies betting opportunities.
///
/// Score combines two independent dimensions:
///   1. EV (expected profit per dollar) — is the bet mathematically worth it?
///   2. Win probability — how often will it win? (affects variance and comfort)
///
/// Scoring (0–10):
///   Base: 5
///   EV bonuses:   +1 at threshold+3%,  +2 at threshold+6%
///   Probability:  +2 if prob > 65%  (comfortable win rate, realises edge quickly)
///                 +1 if prob > 50%  (slight favourite)
///                 −1 if prob < 35%  (longshot — expect losing streaks)
///   Steaming:     +1 (sharp money confirming model)
///   Drifting:     −2, forced SKIP (market disagrees — do not record)
///   BadTiming:    −1
///   HIGH_EDGE:    warning flag only, no score penalty
///   Clamped to [0, 10]
///
/// Decision:
///   GOOD_BET → EV ≥ threshold + score ≥ 6 + no drifting
///   RISKY    → EV ≥ threshold + score < 6  (positive EV but low win prob or concern)
///   SKIP     → EV < threshold OR line drifting
/// </summary>
public class AIValidatorService : IAIValidatorService
{
    private readonly IBettingConfigService _cfg;

    public AIValidatorService(IBettingConfigService cfg) => _cfg = cfg;

    public List<ValidatedBet> Validate(List<BetOpportunity> opportunities)
    {
        var emptyPerMatch = new Dictionary<string, int>();
        return opportunities
            .Select(opp => ValidateOne(opp, emptyPerMatch, parlayMode: false))
            .ToList();
    }

    public List<ValidatedBet> ValidateForParlay(List<BetOpportunity> opportunities)
    {
        var emptyPerMatch = new Dictionary<string, int>();
        return opportunities
            .Select(opp => ValidateOne(opp, emptyPerMatch, parlayMode: true))
            .ToList();
    }

    private ValidatedBet ValidateOne(BetOpportunity opp, Dictionary<string, int> perMatch, bool parlayMode)
    {
        var config = _cfg.Get();
        var flags  = new List<string>();
        int score  = 5;

        double edgeSkip      = config.EdgeThreshold;
        double edgeBonusLow  = config.EdgeThreshold + 0.03;
        double edgeBonusHigh = config.EdgeThreshold + 0.06;

        // ── EV bonuses ────────────────────────────────────────────────────────
        if (opp.Edge > edgeBonusHigh)      score += 2;
        else if (opp.Edge > edgeBonusLow)  score += 1;

        // ── Win probability factor ─────────────────────────────────────────────
        // High prob = lower variance, edge realises faster, more comfortable to hold.
        // Low prob = expect losing streaks even when EV is positive.
        if (opp.Probability > 0.65)       score += 2;
        else if (opp.Probability > 0.50)  score += 1;
        else if (opp.Probability < 0.35)  score -= 1;

        // ── Line movement ─────────────────────────────────────────────────────
        if (opp.LineMovementStatus == "Drifting")
        {
            flags.Add(ValidationFlags.LineMovingAgainst);
            score -= 2;
        }
        else if (opp.LineMovementStatus == "Steaming")
        {
            flags.Add(ValidationFlags.Steaming);
            score += 1;
        }

        // ── Suspiciously high EV — warn only ─────────────────────────────────
        if (opp.Edge > config.HighEdgeThreshold)
            flags.Add(ValidationFlags.HighEdge);

        // ── Timing window ─────────────────────────────────────────────────────
        var hoursUntil = opp.HoursUntilKickoff;
        if (hoursUntil < config.PreMatchMinHours || hoursUntil > config.PreMatchMaxHours)
        {
            flags.Add(ValidationFlags.BadTiming);
            score -= 1;
        }

        score = Math.Clamp(score, 0, 10);

        // ── Decision ──────────────────────────────────────────────────────────
        bool isDrifting = flags.Contains(ValidationFlags.LineMovingAgainst);
        bool skipByEdge = !parlayMode && opp.Edge < edgeSkip;

        string decision;
        if (skipByEdge || isDrifting)
            decision = "SKIP";
        else if (score >= 6)
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
            Reason   = BuildReason(opp, decision, score, flags, edgeSkip)
        };
    }

    private static string BuildReason(
        BetOpportunity opp, string decision, int score,
        List<string> flags, double edgeSkip)
    {
        bool steaming = flags.Contains(ValidationFlags.Steaming);
        bool drifting = flags.Contains(ValidationFlags.LineMovingAgainst);
        bool highEdge = flags.Contains(ValidationFlags.HighEdge);
        bool badTiming = flags.Contains(ValidationFlags.BadTiming);
        string probDesc = opp.Probability > 0.65 ? "high win probability"
                        : opp.Probability > 0.50 ? "slight favourite"
                        : opp.Probability > 0.35 ? "uncertain outcome"
                        : "longshot — expect frequent losses";

        if (decision == "GOOD_BET")
        {
            var note = steaming ? " Sharp money agrees." : "";
            return $"EV +{opp.Edge:P1} · {opp.Probability * 100:F0}% win prob ({probDesc}) · Score {score}/10.{note}";
        }

        if (decision == "SKIP")
        {
            if (opp.Edge < edgeSkip)
                return $"EV {opp.Edge:P1} is below the {edgeSkip:P0} minimum — no value.";
            return "Line drifting — sharp money moving against this selection. Do not record.";
        }

        // RISKY
        var reasons = new List<string>();
        if (opp.Probability < 0.35) reasons.Add($"longshot ({opp.Probability * 100:F0}% win prob) — expect losing streaks, needs 50+ bets to realise edge");
        if (highEdge)   reasons.Add("unusually high EV — verify odds are current");
        if (badTiming)  reasons.Add("outside ideal timing window");
        if (steaming)   reasons.Add("steaming line is a positive sign");
        return $"EV +{opp.Edge:P1} · Score {score}/10 · {string.Join("; ", reasons)}.";
    }
}
