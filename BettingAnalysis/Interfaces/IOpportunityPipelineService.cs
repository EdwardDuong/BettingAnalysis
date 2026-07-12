using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Builds enriched BetOpportunity lists from the current pre-match odds.
/// Encapsulates the shared pipeline used by both the opportunities and parlay endpoints.
/// </summary>
public interface IOpportunityPipelineService
{
    /// <summary>Full pipeline for the /opportunities endpoint — includes pre-validation warnings.</summary>
    Task<List<BetOpportunity>> BuildOpportunitiesAsync(int userId, Bankroll bankroll);

    /// <summary>Parlay pool pipeline — applies ParlayMinEdge filter, skips pre-validation.</summary>
    Task<List<BetOpportunity>> BuildParlayPoolAsync(Bankroll bankroll);
}
