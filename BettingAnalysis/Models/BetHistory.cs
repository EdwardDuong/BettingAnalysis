namespace BettingAnalysis.Models;

/// <summary>
/// Persists every bet including CLV data for long-term model evaluation.
///
/// CLV (Closing Line Value) is the most important long-term metric.
/// Consistently positive CLV = profitable model regardless of short-term results.
/// Industry benchmark: average CLV > +2% indicates a sharp bettor.
/// </summary>
public class BetHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MatchId { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Odds at time of bet placement.</summary>
    public decimal Odds { get; set; }

    /// <summary>
    /// Final market odds just before match started.
    /// Set via POST /Betting/result when recording the outcome.
    /// </summary>
    public decimal? ClosingOdds { get; set; }

    /// <summary>
    /// CLV = (PlacedOdds / ClosingOdds - 1) × 100%.
    /// Null until ClosingOdds is recorded.
    /// Positive = beat the market. Negative = got worse odds than closing.
    /// </summary>
    public double? CLV { get; set; }

    public double Probability { get; set; }
    public double Edge { get; set; }
    public decimal Stake { get; set; }
    public DateTime DateTimePlaced { get; set; } = DateTime.UtcNow;

    /// <summary>"Pending" | "Win" | "Loss"</summary>
    public string Result { get; set; } = "Pending";

    /// <summary>Win: Stake × (Odds − 1). Loss: −Stake. Pending: 0.</summary>
    public decimal PnL { get; set; } = 0;

    public SportType SportType { get; set; }
    public string LineMovementStatus { get; set; } = "Stable";
}
