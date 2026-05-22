using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Central validation gate enforcing ALL professional betting rules.
/// Last line of defense before money is committed.
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// Validates a bet against all 11 professional betting rules.
    /// Returns validation result with violations (hard blocks) and warnings (soft alerts).
    /// </summary>
    ValidationResult Validate(
        MatchOdds match,
        string team,
        decimal odds,
        double edge,
        decimal stake,
        LineMovement lineMovement);
}
