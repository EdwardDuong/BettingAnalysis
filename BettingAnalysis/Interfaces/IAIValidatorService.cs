using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IAIValidatorService
{
    List<ValidatedBet> Validate(List<BetOpportunity> opportunities);
    List<ValidatedBet> ValidateForParlay(List<BetOpportunity> opportunities);
}
