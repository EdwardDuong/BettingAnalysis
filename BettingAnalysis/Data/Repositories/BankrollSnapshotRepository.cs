using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BettingAnalysis.Data.Repositories;

/// <summary>
/// EF Core implementation of IBankrollSnapshotRepository.
/// Handles bankroll historical data for performance analysis.
/// </summary>
public class BankrollSnapshotRepository : IBankrollSnapshotRepository
{
    private readonly BettingDbContext _context;

    public BankrollSnapshotRepository(BettingDbContext context)
    {
        _context = context;
    }

    public async Task<BankrollSnapshot?> GetByIdAsync(int id)
    {
        return await _context.BankrollSnapshots
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IEnumerable<BankrollSnapshot>> GetByUserIdAsync(int userId)
    {
        return await _context.BankrollSnapshots
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SnapshotDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<BankrollSnapshot>> GetByDateRangeAsync(int userId, DateTime startDate, DateTime endDate)
    {
        return await _context.BankrollSnapshots
            .Where(s => s.UserId == userId
                && s.SnapshotDate >= startDate
                && s.SnapshotDate <= endDate)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync();
    }

    public async Task<BankrollSnapshot?> GetLatestSnapshotAsync(int userId)
    {
        return await _context.BankrollSnapshots
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SnapshotDate)
            .FirstOrDefaultAsync();
    }

    public async Task AddAsync(BankrollSnapshot snapshot)
    {
        await _context.BankrollSnapshots.AddAsync(snapshot);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(BankrollSnapshot snapshot)
    {
        _context.BankrollSnapshots.Update(snapshot);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var snapshot = await _context.BankrollSnapshots.FindAsync(id);
        if (snapshot != null)
        {
            _context.BankrollSnapshots.Remove(snapshot);
            await _context.SaveChangesAsync();
        }
    }
}
