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
}
