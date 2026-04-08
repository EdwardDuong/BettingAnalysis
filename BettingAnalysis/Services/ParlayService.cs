using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Builds recommended multi-leg parlay combos from GOOD_BET opportunities.
///
/// Only GOOD_BET selections are eligible legs. Same-match legs are excluded
/// (correlated — violates the independence assumption behind combined probability).
///
/// Sizing: half-Kelly on the combined edge, capped at MaxStakePercent.
/// Combined probability = product of leg probabilities (assumes independence).
/// </summary>
public class ParlayService
{
    private readonly BankrollService      _bankroll;
    private readonly BettingConfigService _cfg;

    private const int MinLegs = 2;
    private const int MaxLegs = 4;
    private const int MaxEligible = 8;  // cap to avoid combinatorial explosion

    public ParlayService(BankrollService bankroll, BettingConfigService cfg)
    {
        _bankroll = bankroll;
        _cfg      = cfg;
    }

    /// <summary>
    /// Build one parlay combo per leg count (2, 3, 4) using the highest-scoring
    /// GOOD_BET selections, excluding same-match duplicates.
    /// Returns an empty list if fewer than 2 GOOD_BET selections are available.
    /// </summary>
    public List<ParlayCombo> BuildCombos(List<BetOpportunity> opportunities)
    {
        var eligible = opportunities
            .Where(o => o.AiValidation?.Decision == "GOOD_BET")
            .OrderByDescending(o => o.AiValidation?.Score ?? 0)
            .ThenByDescending(o => o.Edge)
            .Take(MaxEligible)
            .ToList();

        if (eligible.Count < MinLegs) return new List<ParlayCombo>();

        var config   = _cfg.Get();
        var bankroll = _bankroll.GetBankroll();
        var combos   = new List<ParlayCombo>();

        for (int legs = MinLegs; legs <= Math.Min(MaxLegs, eligible.Count); legs++)
        {
            var combo = BuildBestCombo(eligible, legs, config, bankroll);
            if (combo is not null) combos.Add(combo);
        }

        return combos;
    }

    private static ParlayCombo? BuildBestCombo(
        List<BetOpportunity> eligible,
        int legs,
        BettingConfig config,
        Bankroll bankroll)
    {
        // Greedy: pick highest-scoring legs, one per match
        var selected    = new List<BetOpportunity>();
        var usedMatches = new HashSet<string>();

        foreach (var opp in eligible)
        {
            if (selected.Count >= legs) break;
            if (!usedMatches.Add(opp.MatchId)) continue;
            selected.Add(opp);
        }

        if (selected.Count < legs) return null;

        decimal combinedOdds = selected.Aggregate(1m, (acc, o) => acc * o.Odds);
        double  combinedProb = selected.Aggregate(1.0, (acc, o) => acc * o.Probability);
        double  ev           = (double)combinedOdds * combinedProb - 1.0;

        if (ev <= 0) return null;

        double  kellyFraction = config.KellyFraction;
        double  kellyPct      = ev / ((double)combinedOdds - 1.0) * kellyFraction;
        decimal stake         = Math.Round(
            Math.Min((decimal)kellyPct * bankroll.AvailableBankroll, bankroll.MaxStakePerBet), 2);
        stake = Math.Max(stake, 0);

        return new ParlayCombo
        {
            Legs           = legs,
            RiskLabel      = legs switch { 2 => "Safe", 3 => "Medium", 4 => "Aggressive", _ => "Extreme" },
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
        MatchId     = o.MatchId,
        HomeTeam    = o.HomeTeam,
        AwayTeam    = o.AwayTeam,
        Team        = o.Team,
        Outcome     = o.Outcome,
        Odds        = o.Odds,
        Probability = o.Probability,
        Edge        = o.Edge,
        SportType   = o.SportType.ToString(),
        LineMovement = o.LineMovementStatus,
        AiScore     = o.AiValidation?.Score ?? 5,
        AiDecision  = o.AiValidation?.Decision ?? "RISKY",
        KickoffTime = o.MatchStartTime,
    };
}
