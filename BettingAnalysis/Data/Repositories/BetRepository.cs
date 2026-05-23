using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BettingAnalysis.Data.Repositories;

/// <summary>
/// EF Core implementation of IBetRepository.
/// Handles all bet-related database operations with optimized queries.
/// </summary>
public class BetRepository : IBetRepository
{
    private readonly BettingDbContext _context;

    public BetRepository(BettingDbContext context)
    {
        _context = context;
    }

    // ── CRUD Operations ───────────────────────────────────────────────────────
    public async Task<Bet?> GetByIdAsync(Guid id)
    {
        return await _context.Bets
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<IEnumerable<Bet>> GetAllAsync(int userId)
    {
        return await _context.Bets
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.DateTimePlaced)
            .ToListAsync();
    }

    public async Task<IEnumerable<Bet>> GetPendingBetsAsync(int userId)
    {
        return await _context.Bets
            .Where(b => b.UserId == userId && b.Result == "Pending")
            .ToListAsync();
    }

    public async Task AddAsync(Bet bet)
    {
        await _context.Bets.AddAsync(bet);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Bet bet)
    {
        _context.Bets.Update(bet);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var bet = await _context.Bets.FindAsync(id);
        if (bet != null)
        {
            _context.Bets.Remove(bet);
            await _context.SaveChangesAsync();
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────
    public async Task<IEnumerable<Bet>> GetByMatchIdAsync(string matchId, int userId)
    {
        return await _context.Bets
            .Where(b => b.MatchId == matchId && b.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Bet>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int userId)
    {
        return await _context.Bets
            .Where(b => b.UserId == userId
                && b.DateTimePlaced >= startDate
                && b.DateTimePlaced <= endDate)
            .OrderByDescending(b => b.DateTimePlaced)
            .ToListAsync();
    }

    public async Task<int> CountBetsOnMatchAsync(string matchId, int userId)
    {
        return await _context.Bets
            .CountAsync(b => b.MatchId == matchId && b.UserId == userId);
    }

    public async Task<decimal> GetTotalExposureAsync(int userId)
    {
        return await _context.Bets
            .Where(b => b.UserId == userId && b.Result == "Pending")
            .SumAsync(b => b.Stake);
    }

    // ── Statistics ────────────────────────────────────────────────────────────
    public async Task<int> GetConsecutiveLossesAsync(int userId)
    {
        var recentBets = await _context.Bets
            .Where(b => b.UserId == userId && b.Result != "Pending")
            .OrderByDescending(b => b.DateTimePlaced)
            .Take(50)
            .ToListAsync();

        int consecutiveLosses = 0;
        foreach (var bet in recentBets)
        {
            if (bet.Result == "Loss")
                consecutiveLosses++;
            else
                break;
        }

        return consecutiveLosses;
    }

    public async Task<int> GetCurrentStreakAsync(int userId)
    {
        var recentBets = await _context.Bets
            .Where(b => b.UserId == userId && b.Result != "Pending")
            .OrderByDescending(b => b.DateTimePlaced)
            .Take(50)
            .ToListAsync();

        if (!recentBets.Any())
            return 0;

        var firstResult = recentBets.First().Result;
        return recentBets.TakeWhile(b => b.Result == firstResult).Count();
    }

    public async Task<decimal> GetDailyLossAsync(int userId, DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var losses = await _context.Bets
            .Where(b => b.UserId == userId
                && b.Result == "Loss"
                && b.DateTimePlaced >= startOfDay
                && b.DateTimePlaced < endOfDay)
            .SumAsync(b => Math.Abs(b.PnL));

        return losses;
    }
}
