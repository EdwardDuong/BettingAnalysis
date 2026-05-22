using BettingAnalysis.Interfaces;

namespace BettingAnalysis.Services;

/// <summary>
/// Edge Calculation Service.
///
/// Edge = ModelProbability - ImpliedProbability
///
/// ImpliedProbability = 1 / DecimalOdds
///   (this is the "fair" probability embedded in the odds — includes bookmaker margin)
///
/// Positive edge means our Poisson model estimates the true probability
/// is HIGHER than what the bookmaker is paying for. That is a value bet.
///
/// Rule #2: Only bet when Edge >= 5% (0.05)
/// Rule #8: Flag Edge >= 20% for manual verification (may indicate model error)
/// </summary>
public class EdgeService : IEdgeService
{
    /// <summary>
    /// Calculate the edge for a single outcome.
    /// </summary>
    /// <param name="modelProbability">Probability from Poisson model (0–1)</param>
    /// <param name="bookmakerOdds">Decimal odds (e.g. 2.10 means $1.10 profit per $1 staked)</param>
    /// <returns>Edge as decimal fraction. 0.08 = 8% edge.</returns>
    public double CalculateEdge(double modelProbability, decimal bookmakerOdds)
    {
        // Implied probability embedded in the odds (includes bookmaker's overround)
        double impliedProbability = 1.0 / (double)bookmakerOdds;

        // Our advantage: how much better our model is versus what the market prices
        return modelProbability - impliedProbability;
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
