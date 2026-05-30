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
/// </summary>
public class ParlayService : IParlayService
{
    private readonly IBankrollService      _bankroll;
    private readonly IBettingConfigService _cfg;

    private const int     MinLegs         = 3;
    private const int     MaxLegs         = 5;
    private const int     MaxEligible     = 25;
    private const decimal MinCombinedOdds = 3.0m;

    public ParlayService(IBankrollService bankroll, IBettingConfigService cfg)
    {
        _bankroll = bankroll;
        _cfg      = cfg;
    }

    public async Task<List<ParlayCombo>> BuildCombosAsync(List<BetOpportunity> opportunities)
    {
        var config   = _cfg.Get();
        var bankroll = await _bankroll.GetBankrollAsync();

        var baseEligible = opportunities
            .Where(o => o.AiValidation?.Decision != "SKIP"
                && o.LineMovementStatus != "Drifting"
                && !(o.AiValidation?.Flags?.Contains(ValidationFlags.HighEdge) ?? false)
                && o.Edge >= config.ParlayMinEdge)
            .Take(MaxEligible)
            .ToList();

        if (baseEligible.Count < MinLegs) return [];

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

        return combos;
    }

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
