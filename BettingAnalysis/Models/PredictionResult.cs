namespace BettingAnalysis.Models;

/// <summary>
/// Output of the Poisson model for a given match.
/// All three probabilities must sum to 1.0.
/// DrawProb = 0 for sports without draw markets (AFL, NRL, NBA, Esports).
/// </summary>
public class PredictionResult
{
    public double HomeWinProb { get; set; }
    public double DrawProb { get; set; }
    public double AwayWinProb { get; set; }
}
