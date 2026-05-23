using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IOddsService
{
    List<MatchOdds> GetPreMatchOdds();
    void InvalidateCache();
}
