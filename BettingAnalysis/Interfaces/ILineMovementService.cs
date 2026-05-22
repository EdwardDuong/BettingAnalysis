using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for detecting and analyzing line movement (odds changes).
/// Helps identify sharp money vs public money.
/// </summary>
public interface ILineMovementService
{
    /// <summary>Analyzes line movement for a single outcome.</summary>
    LineMovement GetMovement(decimal currentOdds, decimal? previousOdds);

    /// <summary>Gets human-readable label for line movement.</summary>
    string GetLabel(LineMovement movement);

    /// <summary>Returns true when drifting odds should block the bet.</summary>
    bool ShouldBlock(LineMovement movement);
}
