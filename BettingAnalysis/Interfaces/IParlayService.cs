using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IParlayService
{
    List<ParlayCombo> BuildCombos(List<BetOpportunity> opportunities);
}
