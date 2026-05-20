using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Builds recommended multi-leg parlay combos from the parlay candidate pool.
///
/// Eligibility (evaluated after parlay-mode AI validation):
///   ✅ GOOD_BET and RISKY are both allowed as legs
///   ✅ Edge ≥ ParlayMinEdge (default 2%) per leg
///   ❌ SKIP (truly bad — 3+ flags or score ≤ 2) is excluded
///   ❌ Drifting lines are excluded (risk compounds across legs)
///   ❌ HIGH_EDGE flag excluded (suspicious false-edge risk)
///
/// One combo is built per leg count (2–5). Each combo uses a different
/// sorting strategy so the combos are genuinely distinct:
///   2-leg  → highest AI score  (Safe, most confident picks)
///   3-leg  → highest edge      (Medium, value-focused)
///   4-leg  → highest probability (Aggressive, most likely winners)
///   5-leg  → score + low variance (Extreme, broadest coverage)
///
/// Minimum combined odds: 10.0 (combos below this are skipped).
/// Sizing: half-Kelly on the combined edge, capped at MaxStakePercent.
/// </summary>
public class ParlayService
{
    private readonly BankrollService      _bankroll;
    private readonly BettingConfigService _cfg;

    private const int     MinLegs          = 2;
    private const int     MaxLegs          = 5;
    private const int     MaxEligible      = 25;
    private const decimal MinCombinedOdds  = 10.0m;

    public ParlayService(BankrollService bankroll, BettingConfigService cfg)
    {
        _bankroll = bankroll;
        _cfg      = cfg;
    }

    public List<ParlayCombo> BuildCombos(List<BetOpportunity> opportunities)
    {
        var config   = _cfg.Get();
        var bankroll = _bankroll.GetBankroll();

        // Base eligibility — AI has already done parlay-mode scoring, so SKIP here
        // means truly bad (3+ risk flags or score ≤ 2), not just low-edge.
        var eligible = opportunities
            .Where(o => o.AiValidation?.Decision != "SKIP"
                && o.LineMovementStatus != "Drifting"
                && !(o.AiValidation?.Flags?.Contains(ValidationFlags.HighEdge) ?? false)
                && o.Edge >= config.ParlayMinEdge)
            .Take(MaxEligible)
            .ToList();

        if (eligible.Count < MinLegs) return [];

        // Different sort strategy per leg count — makes each combo genuinely distinct.
        // GOOD_BET legs are always interleaved first within each pool.
        var strategies = new (int Legs, string Label, string Strategy, Func<IEnumerable<BetOpportunity>, IEnumerable<BetOpportunity>> Sort)[]
        {
            (2, "Safe",
             "Highest confidence — best AI-scored picks",
             pool => pool.OrderByDescending(o => o.AiValidation?.Decision == "GOOD_BET" ? 1 : 0)
                         .ThenByDescending(o => o.AiValidation?.Score ?? 0)
                         .ThenByDescending(o => o.Odds)),

            (3, "Medium",
             "Value-focused — highest edge per leg",
             pool => pool.OrderByDescending(o => o.AiValidation?.Decision == "GOOD_BET" ? 1 : 0)
                         .ThenByDescending(o => o.Edge)
                         .ThenByDescending(o => o.AiValidation?.Score ?? 0)),

            (4, "Aggressive",
             "Probability-weighted — most likely winners",
             pool => pool.OrderByDescending(o => o.AiValidation?.Decision == "GOOD_BET" ? 1 : 0)
                         .ThenByDescending(o => o.Probability)
                         .ThenByDescending(o => o.Edge)),

            (5, "Extreme",
             "Broadest coverage — low-variance legs",
             pool => pool.OrderByDescending(o => o.AiValidation?.Score ?? 0)
                         .ThenByDescending(o => o.Edge)
                         .ThenBy(o => o.Odds)),
        };

        var combos = new List<ParlayCombo>();

        foreach (var (legs, label, strategy, sort) in strategies)
        {
            if (eligible.Count < legs) continue;
            var combo = BuildBestCombo(sort(eligible).ToList(), legs, label, strategy, config, bankroll);
            if (combo is not null) combos.Add(combo);
        }

        return combos;
    }

    private static ParlayCombo? BuildBestCombo(
        List<BetOpportunity> pool,
        int legs,
        string riskLabel,
        string strategy,
        BettingConfig config,
        Bankroll bankroll)
    {
        // Greedy: pick highest-priority legs, one per match (no correlated outcomes)
        var selected    = new List<BetOpportunity>();
        var usedMatches = new HashSet<string>();

        foreach (var opp in pool)
        {
            if (selected.Count >= legs) break;
            if (!usedMatches.Add(opp.MatchId)) continue;
            selected.Add(opp);
        }

        if (selected.Count < legs) return null;

        decimal combinedOdds = selected.Aggregate(1m, (acc, o) => acc * o.Odds);
        if (combinedOdds < MinCombinedOdds) return null;

        double combinedProb = selected.Aggregate(1.0, (acc, o) => acc * o.Probability);
        double ev           = (double)combinedOdds * combinedProb - 1.0;

        if (ev <= 0) return null;

        double kellyPct = ev / ((double)combinedOdds - 1.0) * config.KellyFraction;
        decimal stake   = Math.Round(
            Math.Min((decimal)kellyPct * bankroll.AvailableBankroll, bankroll.MaxStakePerBet), 2);
        stake = Math.Max(stake, 0);

        return new ParlayCombo
        {
            Legs           = legs,
            RiskLabel      = riskLabel,
            Strategy       = strategy,
            CombinedOdds   = Math.Round(combinedOdds, 2),
            CombinedProb   = Math.Round(combinedProb, 4),
            ExpectedValue  = Math.Round(ev, 4),
            AvgEdge        = Math.Round(selected.Average(o => o.Edge), 4),
            SuggestedStake = stake,
            AvgAiScore     = Math.Round(selected.Average(o => (double)(o.AiValidation?.Score ?? 5)), 1),
            Selections     = selected.Select(ToParlayLeg).ToList()
        };
    }

    private static ParlayLeg ToParlayLeg(BetOpportunity o) => new()
    {
        MatchId      = o.MatchId,
        HomeTeam     = o.HomeTeam,
        AwayTeam     = o.AwayTeam,
        Team         = o.Team,
        Outcome      = o.Outcome,
        Odds         = o.Odds,
        Probability  = o.Probability,
        Edge         = o.Edge,
        SportType    = o.SportType.ToString(),
        LineMovement = o.LineMovementStatus,
        AiScore      = o.AiValidation?.Score ?? 5,
        AiDecision   = o.AiValidation?.Decision ?? "RISKY",
        KickoffTime  = o.MatchStartTime,
    };
}
