using BettingAnalysis.Data.Entities;
using BettingAnalysis.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAnalysis.Data;

/// <summary>
/// EF Core DbContext for BettingAnalysis system.
/// Manages Bet, User, and BankrollSnapshot entities with proper relationships and constraints.
/// </summary>
public class BettingDbContext : DbContext
{
    public BettingDbContext(DbContextOptions<BettingDbContext> options) : base(options) { }

    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BankrollSnapshot> BankrollSnapshots => Set<BankrollSnapshot>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User Entity Configuration ─────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasIndex(e => e.Username).IsUnique();

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Email).IsUnique();

            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("User");

            entity.Property(e => e.InitialBankroll)
                .HasPrecision(18, 2);

            entity.Property(e => e.CurrentBankroll)
                .HasPrecision(18, 2);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);
        });

        // ── Bet Entity Configuration ──────────────────────────────────────────
        modelBuilder.Entity<Bet>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MatchId)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.MatchId);

            entity.Property(e => e.HomeTeam)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.AwayTeam)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Team)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Outcome)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Odds)
                .HasPrecision(18, 2);

            entity.Property(e => e.ClosingOdds)
                .HasPrecision(18, 2);

            entity.Property(e => e.Stake)
                .HasPrecision(18, 2);

            entity.Property(e => e.PnL)
                .HasPrecision(18, 2);

            entity.Property(e => e.DateTimePlaced)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.Result)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.Property(e => e.LineMovementStatus)
                .HasMaxLength(50)
                .HasDefaultValue("Stable");

            entity.Property(e => e.SportType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(e => e.Result);
            entity.HasIndex(e => e.DateTimePlaced);

            // ── Foreign Key to User ───────────────────────────────────────────
            entity.HasOne(e => e.User)
                .WithMany(u => u.Bets)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── BankrollSnapshot Entity Configuration ─────────────────────────────
        modelBuilder.Entity<BankrollSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TotalBankroll)
                .HasPrecision(18, 2);

            entity.Property(e => e.AvailableBankroll)
                .HasPrecision(18, 2);

            entity.Property(e => e.TotalExposure)
                .HasPrecision(18, 2);

            entity.Property(e => e.DailyLossUsed)
                .HasPrecision(18, 2);

            entity.Property(e => e.CumulativeLoss)
                .HasPrecision(18, 2);

            entity.Property(e => e.TotalPnL)
                .HasPrecision(18, 2);

            entity.Property(e => e.SnapshotDate)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.SnapshotDate);
            entity.HasIndex(e => new { e.UserId, e.SnapshotDate });

            // ── Foreign Key to User ───────────────────────────────────────────
            entity.HasOne(e => e.User)
                .WithMany(u => u.BankrollSnapshots)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RefreshToken Entity Configuration ─────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Token)
                .IsRequired()
                .HasMaxLength(256);

            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.IsRevoked });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Default admin user is seeded at startup by DataSeeder (not here) so
        // BCrypt can hash the password at runtime rather than embedding a plain-text value.
    }
}
