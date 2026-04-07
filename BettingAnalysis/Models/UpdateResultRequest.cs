namespace BettingAnalysis.Models;

/// <summary>
/// Request body for POST /Betting/result/{id}.
/// ClosingOdds enables CLV calculation for the bet.
/// </summary>
public class UpdateResultRequest
{
    /// <summary>"Win" or "Loss"</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>
    /// Final bookmaker odds just before match started.
    /// Used to compute CLV = (PlacedOdds / ClosingOdds - 1) * 100%.
    /// Leave null if closing odds are unknown.
    /// </summary>
    public decimal? ClosingOdds { get; set; }
}
