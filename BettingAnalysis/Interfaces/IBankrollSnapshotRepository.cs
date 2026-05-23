using BettingAnalysis.Data.Entities;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Repository interface for BankrollSnapshot entity operations.
/// Enables performance tracking and historical analysis.
/// </summary>
public interface IBankrollSnapshotRepository
{
    Task<BankrollSnapshot?> GetByIdAsync(int id);
    Task<IEnumerable<BankrollSnapshot>> GetByUserIdAsync(int userId);
    Task<IEnumerable<BankrollSnapshot>> GetByDateRangeAsync(int userId, DateTime startDate, DateTime endDate);
    Task<BankrollSnapshot?> GetLatestSnapshotAsync(int userId);
    Task AddAsync(BankrollSnapshot snapshot);
    Task UpdateAsync(BankrollSnapshot snapshot);
    Task DeleteAsync(int id);
}
