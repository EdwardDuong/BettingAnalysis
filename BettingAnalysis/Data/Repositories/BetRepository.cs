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

    public async Task<(IEnumerable<Bet> Items, int Total)> GetPagedAsync(int userId, int page, int pageSize)
    {
        var query = _context.Bets
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.DateTimePlaced);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
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
        var count       = recentBets.TakeWhile(b => b.Result == firstResult).Count();
        // positive = win streak, negative = loss streak
        return firstResult == "Win" ? count : -count;
    }

    public async Task<decimal> GetDailyLossAsync(int userId, DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay   = startOfDay.AddDays(1);

        return await _context.Bets
            .Where(b => b.UserId == userId
                && b.Result == "Loss"
                && b.DateTimePlaced >= startOfDay
                && b.DateTimePlaced < endOfDay)
            .SumAsync(b => Math.Abs(b.PnL));
    }

    public async Task<(int Total, int Wins, int Losses, decimal TotalPnL, double AvgCLV)> GetSettledStatsAsync(int userId)
    {
        var settled = _context.Bets
            .Where(b => b.UserId == userId && b.Result != "Pending");

        var total  = await settled.CountAsync();
        var wins   = await settled.CountAsync(b => b.Result == "Win");
        var pnl    = total > 0 ? await settled.SumAsync(b => b.PnL) : 0m;
        var avgClv = total > 0
            ? await settled.Where(b => b.CLV.HasValue).AverageAsync(b => (double?)b.CLV) ?? 0.0
            : 0.0;

        return (total, wins, total - wins, pnl, avgClv);
    }

    public async Task<decimal> GetTotalStakedAsync(int userId)
        => await _context.Bets
            .Where(b => b.UserId == userId && b.Result != "Pending")
            .SumAsync(b => (decimal?)b.Stake) ?? 0m;

    public async Task<double?> GetAverageEdgeAsync(int userId)
    {
        var settled = _context.Bets.Where(b => b.UserId == userId && b.Result != "Pending");
        var count   = await settled.CountAsync();
        return count > 0 ? await settled.AverageAsync(b => (double?)b.Edge) : null;
    }

    public async Task<List<BettingAnalysis.Models.SettledBetSlice>> GetSettledSlicesAsync(int userId)
        => await _context.Bets
            .Where(b => b.UserId == userId && b.Result != "Pending")
            .Select(b => new BettingAnalysis.Models.SettledBetSlice(b.SportType, b.Result, b.Probability, b.PnL, b.Edge, b.CLV))
            .ToListAsync();
}
