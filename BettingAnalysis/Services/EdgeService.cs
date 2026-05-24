using BettingAnalysis.Interfaces;

namespace BettingAnalysis.Services;

/// <summary>
/// Edge Calculation Service.
///
/// We use Expected Value (EV) not raw probability gap.
///
/// EV = (ModelProbability × DecimalOdds) − 1
///
/// Why EV and not (model_prob − implied_prob)?
///   A 3% probability gap at 1.40 odds → EV = +4.2%
///   A 3% probability gap at 4.00 odds → EV = +12.0%
/// The same probability edge produces wildly different dollar returns at
/// different odds. Filtering on probability gap unfairly rejects high-odds
/// value bets. EV captures what actually matters: profit per dollar staked.
///
/// Threshold (EdgeThreshold in config): 4% EV is a reasonable minimum.
/// Suspicious flag (HighEdgeThreshold): >20% EV suggests stale/wrong odds.
/// </summary>
public class EdgeService : IEdgeService
{
    /// <summary>
    /// Calculate the expected value (EV) for a single outcome.
    /// </summary>
    /// <param name="modelProbability">Probability from Poisson model (0–1)</param>
    /// <param name="bookmakerOdds">Decimal odds (e.g. 2.10 = $1.10 profit per $1 staked)</param>
    /// <returns>EV as decimal fraction. 0.08 = +8% return per dollar staked.</returns>
    public double CalculateEdge(double modelProbability, decimal bookmakerOdds)
    {
        return modelProbability * (double)bookmakerOdds - 1.0;
    }

    /// <summary>
    /// Calculate the total bookmaker overround (vig/juice) for a match.
    /// A fair book sums to 1.0; overround > 1.0 indicates bookmaker margin.
    /// Typical range: 1.03 (3% margin) to 1.10+ for smaller markets.
    /// </summary>
    public double CalculateOverround(decimal homeOdds, decimal? drawOdds, decimal awayOdds)
    {
        double overround = (1.0 / (double)homeOdds) + (1.0 / (double)awayOdds);
        if (drawOdds.HasValue)
            overround += 1.0 / (double)drawOdds.Value;
        return overround;
    }
}
