namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for calculating optimal bet stake using Kelly Criterion.
/// Applies fractional Kelly for risk management.
/// </summary>
public interface IBetSizingService
{
    /// <summary>
    /// Calculates Kelly stake with fractional multiplier and hard cap.
    /// Returns 0 if edge is negative or negligible.
    /// </summary>
    decimal CalculateStake(double probability, decimal odds, decimal bankroll);
}
