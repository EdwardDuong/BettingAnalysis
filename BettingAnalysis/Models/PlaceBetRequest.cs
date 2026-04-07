namespace BettingAnalysis.Models;

/// <summary>
/// Request body for POST /Betting/place.
/// </summary>
public class PlaceBetRequest
{
    /// <summary>Match identifier from BetOpportunity.</summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>"Home", "Draw", or "Away".</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// Optional override stake in dollars.
    /// If null, the system uses the Kelly-calculated SuggestedStake.
    /// Still capped at MaxStakePerBet regardless.
    /// </summary>
    public decimal? CustomStake { get; set; }
}
