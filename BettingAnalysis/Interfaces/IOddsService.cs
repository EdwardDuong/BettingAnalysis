using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IOddsService
{
    Task<List<MatchOdds>> GetPreMatchOddsAsync();
    void InvalidateCache();
}
