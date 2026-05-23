namespace BettingAnalysis.Data.Entities;

/// <summary>
/// Database entity for user account management.
/// Supports individual use with potential for multi-user expansion.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // ── Role-based access ─────────────────────────────────────────────────────
    /// <summary>"Admin" | "User"</summary>
    public string Role { get; set; } = "User";

    // ── Bankroll tracking ─────────────────────────────────────────────────────
    public decimal InitialBankroll { get; set; }
    public decimal CurrentBankroll { get; set; }

    // ── Metadata ──────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // ── Navigation properties ─────────────────────────────────────────────────
    public virtual ICollection<Bet> Bets { get; set; } = new List<Bet>();
    public virtual ICollection<BankrollSnapshot> BankrollSnapshots { get; set; } = new List<BankrollSnapshot>();
}
