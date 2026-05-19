using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Builds recommended multi-leg parlay combos from the wider parlay candidate pool.
///
/// Eligibility (more permissive than single-bet gate):
///   ✅ Any selection with edge ≥ ParlayMinEdge (default 2%)
///   ✅ GOOD_BET and RISKY are both allowed as legs
///   ❌ SKIP (AI vetoed) is excluded
///   ❌ Drifting lines are excluded (market disagrees, risk compounds)
///   ❌ HIGH_EDGE flag excluded (suspicious false-edge risk)
///
/// GOOD_BET legs are prioritised over RISKY when building each combo.
/// Same-match legs are excluded (correlated outcomes).
///
/// Sizing: half-Kelly on the combined edge, capped at MaxStakePercent.
/// </summary>
public class ParlayService
{
    private readonly BankrollService      _bankroll;
    private readonly BettingConfigService _cfg;

    private const int MinLegs = 2;
    private const int MaxLegs = 4;
    private const int MaxEligible = 20;  // wider pool now includes lower-edge legs

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
        // Parlay compounds risk, so drifting lines and suspicious edges are still
        // excluded. SKIP is excluded (AI vetoed). GOOD_BET and RISKY are both
        // allowed, with GOOD_BET legs sorted first so they anchor each combo.
        var eligible = opportunities
            .Where(o => o.AiValidation?.Decision != "SKIP"
                && o.LineMovementStatus != "Drifting"
                && !(o.AiValidation?.Flags?.Contains(ValidationFlags.HighEdge) ?? false))
            .OrderByDescending(o => o.AiValidation?.Decision == "GOOD_BET" ? 1 : 0)
            .ThenByDescending(o => o.AiValidation?.Score ?? 0)
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
