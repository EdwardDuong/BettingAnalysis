using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Builds recommended multi-leg parlay combos from the parlay candidate pool.
///
/// Tier rules (minimum 3 legs):
///   Safe (3-leg)       → favourites only (prob ≥ 55%), GOOD_BET score ≥ 6
///                        min combined prob: 20%  (≈ three 58% legs)
///   Medium (4-leg)     → GOOD_BET only, any probability
///                        min combined prob: 12%
///   Aggressive (5-leg) → GOOD_BET score ≥ 7
///                        min combined prob: 6%
///
/// A combo is rejected if its combined probability is below the tier floor —
/// this prevents "Safe" being labelled on a 14% win-rate parlay.
/// Sizing: half-Kelly capped at per-tier stake limits.
///
/// BuildDailyDoubleAsync is a separate, differently-shaped pick: instead of a
/// fixed leg count per risk tier, it finds the safest way (by realized win
/// probability) to clear a target combined odds (default 2x, "double"),
/// combining as few or as many legs (up to DailyDoubleMaxLegs) as that requires
/// — including just one, when a single leg already clears the target and that's
/// safer than any parlay would be.
/// </summary>
public class ParlayService : IParlayService
{
    private readonly IBettingConfigService _cfg;

    private const int     MinLegs         = 3;
    private const int     MaxLegs         = 5;
    private const int     MaxEligible     = 25;
    private const decimal MinCombinedOdds = 3.0m;

    public ParlayService(IBettingConfigService cfg)
    {
        _cfg = cfg;
    }

    public Task<List<ParlayCombo>> BuildCombosAsync(List<BetOpportunity> opportunities, Bankroll bankroll)
    {
        var config = _cfg.Get();
        var baseEligible = GetBaseEligible(opportunities, config);

        if (baseEligible.Count < MinLegs) return Task.FromResult(new List<ParlayCombo>());

        var goodBet       = baseEligible.Where(o => o.AiValidation?.Decision == "GOOD_BET").ToList();
        var goodBetScore7 = goodBet.Where(o => (o.AiValidation?.Score ?? 0) >= 7).ToList();

        var safePool = baseEligible
            .Where(o => o.Probability >= 0.55 && (o.AiValidation?.Score ?? 0) >= 6)
            .ToList();

        var strategies = new (int Legs, string Label, string Strategy,
            double MinCombinedProb,
            List<BetOpportunity> Pool,
            Func<IEnumerable<BetOpportunity>, IEnumerable<BetOpportunity>> Sort)[]
        {
            (3, "Safe",
             "Three favourites (prob ≥ 55%, score ≥ 6) — comfortable win rate",
             0.20,
             safePool,
             pool => pool.OrderByDescending(o => o.Probability)
                         .ThenByDescending(o => o.AiValidation?.Score ?? 0)),

            (4, "Medium",
             "Four GOOD_BET legs — probability-weighted for best hit rate",
             0.12,
             goodBet,
             pool => pool.OrderByDescending(o => o.Probability)
                         .ThenByDescending(o => o.Edge)),

            (5, "Aggressive",
             "Five GOOD_BET legs (score ≥ 7) — maximum bonus, accept lower hit rate",
             0.06,
             goodBetScore7,
             pool => pool.OrderByDescending(o => o.AiValidation?.Score ?? 0)
                         .ThenByDescending(o => o.Edge)),
        };

        var combos = new List<ParlayCombo>();

        foreach (var (legs, label, strategy, minCombProb, pool, sort) in strategies)
        {
            if (pool.Count < legs) continue;
            var combo = BuildBestCombo(sort(pool).ToList(), legs, label, strategy, minCombProb, config, bankroll);
            if (combo is not null) combos.Add(combo);
        }

        return Task.FromResult(combos);
    }

    public Task<ParlayCombo?> BuildDailyDoubleAsync(List<BetOpportunity> opportunities, Bankroll bankroll)
    {
        var config = _cfg.Get();
        var baseEligible = GetBaseEligible(opportunities, config);
        if (baseEligible.Count == 0) return Task.FromResult<ParlayCombo?>(null);

        // Greedy by "odds gained per unit of probability spent" — log(odds)/-log(prob)
        // — not by raw probability. A leg with lower probability but much higher odds
        // can clear the target using fewer/cheaper legs than several very-safe-but-
        // short-odds legs stacked together; sorting by probability alone would miss
        // that. This naturally reduces to picking a single leg whenever that's the
        // efficient choice (see class doc). It's a greedy heuristic, not an exact
        // solver — selecting the true safest subset is a 0/1 knapsack problem, NP-hard
        // to solve exactly over a 25-candidate pool.
        var byEfficiency = baseEligible.OrderByDescending(Efficiency).ToList();

        var selected      = new List<BetOpportunity>();
        var usedMatches   = new HashSet<string>();
        decimal combinedOdds = 1m;

        foreach (var opp in byEfficiency)
        {
            if (combinedOdds >= config.DailyDoubleTargetOdds) break;
            if (selected.Count >= config.DailyDoubleMaxLegs) break;
            if (opp.Odds <= 1m) continue;               // no payout margin — never useful
            if (!usedMatches.Add(opp.MatchId)) continue; // one leg per match, avoids correlated same-match legs

            selected.Add(opp);
            combinedOdds *= opp.Odds;
        }

        if (selected.Count == 0 || combinedOdds < config.DailyDoubleTargetOdds)
            return Task.FromResult<ParlayCombo?>(null); // not enough good legs today to safely clear the target

        return Task.FromResult(BuildDoublePick(selected, config, bankroll));
    }

    /// <summary>Odds gained (log scale) per unit of win-probability given up. Higher is more "efficient".</summary>
    private static double Efficiency(BetOpportunity o)
    {
        var odds = (double)o.Odds;
        if (odds <= 1.0) return double.NegativeInfinity;
        var prob = Math.Clamp(o.Probability, 0.0001, 0.9999);
        return Math.Log(odds) / -Math.Log(prob);
    }

    private static ParlayCombo? BuildDoublePick(List<BetOpportunity> selected, BettingConfig config, Bankroll bankroll)
    {
        decimal combinedOdds = selected.Aggregate(1m, (acc, o) => acc * o.Odds);
        double combinedProb  = selected.Aggregate(1.0, (acc, o) => acc * o.Probability);
        double ev = (double)combinedOdds * combinedProb - 1.0;
        if (ev <= 0) return null;

        double kellyPct = ev / ((double)combinedOdds - 1.0) * config.KellyFraction;
        decimal stake   = Math.Max(0m, Math.Round(
            Math.Min((decimal)kellyPct * bankroll.AvailableBankroll, config.DailyDoubleMaxStake), 2));

        return new ParlayCombo
        {
            Legs           = selected.Count,
            RiskLabel      = selected.Count == 1 ? "Single" : "Parlay",
            Strategy       = $"Fewest/safest legs found to clear {config.DailyDoubleTargetOdds:0.00}x combined odds " +
                              "— uses a single leg instead of a parlay whenever that's actually the safer way to reach it.",
            CombinedOdds   = Math.Round(combinedOdds, 2),
            CombinedProb   = Math.Round(combinedProb, 4),
            ExpectedValue  = Math.Round(ev, 4),
            AvgEdge        = Math.Round(selected.Average(o => o.Edge), 4),
            SuggestedStake = stake,
            AvgAiScore     = Math.Round(selected.Average(o => (double)(o.AiValidation?.Score ?? 5)), 1),
            Selections     = selected.Select(ToParlayLeg).ToList(),
        };
    }

    private static List<BetOpportunity> GetBaseEligible(List<BetOpportunity> opportunities, BettingConfig config) =>
        opportunities
            .Where(o => o.AiValidation?.Decision != "SKIP"
                && o.LineMovementStatus != "Drifting"
                && !(o.AiValidation?.Flags?.Contains(ValidationFlags.HighEdge) ?? false)
                && o.Edge >= config.ParlayMinEdge)
            .Take(MaxEligible)
            .ToList();

    private static ParlayCombo? BuildBestCombo(
        List<BetOpportunity> pool,
        int legs,
        string riskLabel,
        string strategy,
        double minCombinedProb,
        BettingConfig config,
        Bankroll bankroll)
    {
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
        if (combinedProb < minCombinedProb) return null;

        double ev = (double)combinedOdds * combinedProb - 1.0;
        if (ev <= 0) return null;

        decimal parlayMaxStake = legs switch
        {
            3 => config.Parlay3MaxStake,
            4 => config.Parlay4MaxStake,
            _ => config.Parlay5MaxStake,
        };
        double kellyPct = ev / ((double)combinedOdds - 1.0) * config.KellyFraction;
        decimal stake   = Math.Round(
            Math.Min((decimal)kellyPct * bankroll.AvailableBankroll, parlayMaxStake), 2);
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
