using BettingAnalysis.Data.Entities;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Repository interface for Bet entity operations.
/// Provides abstraction over data access layer for testability and flexibility.
/// </summary>
public interface IBetRepository
{
    // ── CRUD Operations ───────────────────────────────────────────────────────
    Task<Bet?> GetByIdAsync(Guid id);
    Task<IEnumerable<Bet>> GetAllAsync(int userId);
    Task<(IEnumerable<Bet> Items, int Total)> GetPagedAsync(int userId, int page, int pageSize);
    Task<IEnumerable<Bet>> GetPendingBetsAsync(int userId);
    Task AddAsync(Bet bet);
    Task UpdateAsync(Bet bet);
    Task DeleteAsync(Guid id);

    // ── Queries ───────────────────────────────────────────────────────────────
    Task<IEnumerable<Bet>> GetByMatchIdAsync(string matchId, int userId);
    Task<IEnumerable<Bet>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int userId);
    Task<int> CountBetsOnMatchAsync(string matchId, int userId);
    Task<decimal> GetTotalExposureAsync(int userId);

    // ── Statistics ────────────────────────────────────────────────────────────
    Task<int> GetConsecutiveLossesAsync(int userId);
    Task<int> GetCurrentStreakAsync(int userId);
    Task<decimal> GetDailyLossAsync(int userId, DateTime date);

    /// <summary>Single SQL pass over settled bets — avoids full table fetch for snapshot writing.</summary>
    Task<(int Total, int Wins, int Losses, decimal TotalPnL, double AvgCLV)> GetSettledStatsAsync(int userId);

    /// <summary>Total stake wagered on all settled bets.</summary>
    Task<decimal> GetTotalStakedAsync(int userId);

    /// <summary>Average edge (as a fraction) across all settled bets; null if no history.</summary>
    Task<double?> GetAverageEdgeAsync(int userId);

    /// <summary>Slim projection of settled bets for per-sport grouping — no full entity load.</summary>
    Task<List<BettingAnalysis.Models.SettledBetSlice>> GetSettledSlicesAsync(int userId);
}
