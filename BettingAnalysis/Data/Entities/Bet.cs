using BettingAnalysis.Models;

namespace BettingAnalysis.Data.Entities;

/// <summary>
/// Database entity for persisted bet records.
/// Maps to BetHistory domain model with added UserId for multi-user support.
/// </summary>
public class Bet
{
    public Guid Id { get; set; }
    public int UserId { get; set; }

    // ── Match info ────────────────────────────────────────────────────────────
    public string MatchId { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public SportType SportType { get; set; }

    // ── Odds & value ──────────────────────────────────────────────────────────
    public decimal Odds { get; set; }
    public decimal? ClosingOdds { get; set; }
    public double? CLV { get; set; }
    public double Probability { get; set; }
    public double Edge { get; set; }

    // ── Bet execution ─────────────────────────────────────────────────────────
    public decimal Stake { get; set; }
    public DateTime DateTimePlaced { get; set; }
    public string LineMovementStatus { get; set; } = "Stable";

    // ── Result ────────────────────────────────────────────────────────────────
    /// <summary>"Pending" | "Win" | "Loss"</summary>
    public string Result { get; set; } = "Pending";
    public decimal PnL { get; set; } = 0;

    // ── Navigation properties ─────────────────────────────────────────────────
    public virtual User User { get; set; } = null!;
}
