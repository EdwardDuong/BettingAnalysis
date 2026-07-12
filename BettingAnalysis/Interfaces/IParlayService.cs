using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IParlayService
{
    Task<List<ParlayCombo>> BuildCombosAsync(List<BetOpportunity> opportunities, Bankroll bankroll);
}
