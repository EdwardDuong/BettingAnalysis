using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// AI Validation Layer — scores and classifies betting opportunities.
///
/// Score (0–10):
///   Base: 5
///   EV:          +1 at threshold+3%,  +2 at threshold+6%
///   Probability: +2 if prob > 65%  (comfortable win rate, realises edge quickly)
///                +1 if prob > 50%  (slight favourite)
///                −1 if prob < 35%  (longshot — expect losing streaks)
///   Steaming:    +1 (sharp money confirming model)
///   Drifting:    −2, forced SKIP
///   HighEdge:    −2 (suspiciously high EV — likely stale odds or data error)
///   OddsTooLow:  −1 (odds < 1.5 — no payout margin for error)
///   HighVariance: −1 (odds > 3.0 — high bankroll volatility)
///   EplLowEdge:  −1 (EPL + edge < 8% — liquid market, thin edge not enough)
///   CorrelatedBet: −1 (multiple selections on same match)
///   BadTiming:   −1
///
/// Decision:
///   GOOD_BET → score ≥ 6 + not drifting + edge ≥ threshold (non-parlay)
///   RISKY    → score < 6 (positive EV but concern)
///   SKIP     → edge < threshold (non-parlay) OR line drifting
/// </summary>
public class AIValidatorService : IAIValidatorService
{
    private readonly IBettingConfigService _cfg;

    public AIValidatorService(IBettingConfigService cfg) => _cfg = cfg;

    public List<ValidatedBet> Validate(List<BetOpportunity> opportunities)
    {
        var perMatch = opportunities
            .GroupBy(o => o.MatchId)
            .ToDictionary(g => g.Key, g => g.Count());

        return opportunities
            .Select(opp => ValidateOne(opp, perMatch, parlayMode: false))
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
        if (opp.Edge > edgeBonusHigh)     score += 2;
        else if (opp.Edge > edgeBonusLow) score += 1;

        // ── Win probability ───────────────────────────────────────────────────
        if (opp.Probability > 0.65)      score += 2;
        else if (opp.Probability > 0.50) score += 1;
        else if (opp.Probability < 0.35) score -= 1;

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

        // ── Suspiciously high EV ──────────────────────────────────────────────
        if (opp.Edge > config.HighEdgeThreshold)
        {
            flags.Add(ValidationFlags.HighEdge);
            score -= 2;
        }

        // ── Odds sanity ───────────────────────────────────────────────────────
        if (opp.Odds < 1.5m)
        {
            flags.Add(ValidationFlags.OddsTooLow);
            score -= 1;
        }
        else if (opp.Odds > 3.0m)
        {
            flags.Add(ValidationFlags.HighVariance);
            score -= 1;
        }

        // ── EPL thin-edge warning ─────────────────────────────────────────────
        if (opp.SportType == SportType.EPL && opp.Edge < 0.08)
        {
            flags.Add(ValidationFlags.EplLowEdge);
            score -= 1;
        }

        // ── Timing window ─────────────────────────────────────────────────────
        if (opp.HoursUntilKickoff < config.PreMatchMinHours ||
            opp.HoursUntilKickoff > config.PreMatchMaxHours)
        {
            flags.Add(ValidationFlags.BadTiming);
            score -= 1;
        }

        // ── Correlated bets (non-parlay only — parlay handled structurally) ───
        if (!parlayMode && perMatch.TryGetValue(opp.MatchId, out var matchCount) && matchCount > 1)
        {
            flags.Add(ValidationFlags.CorrelatedBet);
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
        bool steaming  = flags.Contains(ValidationFlags.Steaming);
        bool drifting  = flags.Contains(ValidationFlags.LineMovingAgainst);
        bool highEdge  = flags.Contains(ValidationFlags.HighEdge);
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
            if (drifting) return "Line drifting — sharp money moving against this selection. Do not record.";
            return $"EV {opp.Edge:P1} is below the {edgeSkip:P0} minimum — no value.";
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
