using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for calculating outcome probabilities using Poisson distribution.
/// Supports both full matrix (EPL with draws) and binary outcomes (AFL/NRL/NBA/Esports).
/// </summary>
public interface IPoissonService
{
    /// <summary>Predicts match outcome probabilities using Poisson model.</summary>
    PredictionResult Predict(MatchOdds match);
}
