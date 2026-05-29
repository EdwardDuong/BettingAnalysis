using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

public interface IValidationService
{
    Task<ValidationResult> ValidateAsync(
        MatchOdds    match,
        string       team,
        decimal      odds,
        double       edge,
        decimal      stake,
        LineMovement lineMovement);
}
