namespace BettingAnalysis.Models;

/// <summary>Projection used for per-sport stats and calibration aggregation — avoids loading full Bet entities.</summary>
public record SettledBetSlice(SportType SportType, string Result, double Probability, decimal PnL, double Edge, double? CLV);

public enum SportType { EPL, AFL, NRL, NBA, Esports, LaLiga, Bundesliga, SerieA, Ligue1 }

/// <summary>
/// A pre-match fixture with current and previous bookmaker odds.
/// Previous odds enable line movement detection (LineMovementService).
/// Lambda values feed the Poisson probability model (PoissonService).
/// </summary>
public class MatchOdds
{
    public string MatchId { get; set; } = Guid.NewGuid().ToString();
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;

    // ── Current odds ──────────────────────────────────────────────────────────
    public decimal HomeOdds { get; set; }
    public decimal? DrawOdds { get; set; }
    public decimal AwayOdds { get; set; }

    // ── Previous odds (1–2 hours ago) for line movement detection ─────────────
    public decimal? PreviousHomeOdds { get; set; }
    public decimal? PreviousDrawOdds { get; set; }
    public decimal? PreviousAwayOdds { get; set; }

    public DateTime MatchStartTime { get; set; }
    public SportType SportType { get; set; }

    // ── Poisson model inputs ──────────────────────────────────────────────────
    public double HomeLambda { get; set; } = 1.4;
    public double AwayLambda { get; set; } = 1.1;
}
