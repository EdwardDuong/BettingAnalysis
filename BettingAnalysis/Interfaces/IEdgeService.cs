namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for calculating betting edge (model probability vs market implied probability).
/// Positive edge = value bet opportunity.
/// </summary>
public interface IEdgeService
{
    /// <summary>
    /// Calculates edge percentage.
    /// Returns positive value when model probability exceeds market implied probability.
    /// </summary>
    double CalculateEdge(double modelProbability, decimal odds);
}
