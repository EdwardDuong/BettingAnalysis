using System.Collections.Concurrent;
using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public class BettingLoggingService : IBettingLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, List<RejectedBet>> _rejected = new();
    private readonly object _rejectedLock = new();

    public BettingLoggingService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    // ── Placed bets ───────────────────────────────────────────────────────────

    public async Task LogBetAsync(int userId, BetHistory bet)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        await repo.AddAsync(MapToEntity(bet, userId));
    }

    public async Task<List<BetHistory>> GetHistoryAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bets = await repo.GetAllAsync(userId);
        return bets.Select(MapToDomain).ToList();
    }

    public async Task<(List<BetHistory> Items, int Total)> GetHistoryPagedAsync(int userId, int page, int pageSize)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo            = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var (bets, total)   = await repo.GetPagedAsync(userId, page, pageSize);
        return (bets.Select(MapToDomain).ToList(), total);
    }

    public async Task<BetHistory?> GetByIdAsync(Guid id, int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bet = await repo.GetByIdAsync(id);
        return bet is null || bet.UserId != userId ? null : MapToDomain(bet);
    }

    public async Task UpdateResultAsync(Guid id, int userId, string result, decimal pnl, decimal? closingOdds, double? clv)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bet  = await repo.GetByIdAsync(id);
        if (bet is null || bet.UserId != userId) return;

        bet.Result      = result;
        bet.PnL         = pnl;
        bet.ClosingOdds = closingOdds;
        bet.CLV         = clv;
        await repo.UpdateAsync(bet);
    }

    // ── Rejected bets ─────────────────────────────────────────────────────────
    // In-memory only (not persisted) — acceptable to lose on restart, but must
    // still be scoped per user so one user can't see another's rejection log.

    public void LogRejected(int userId, string matchId, string team, string outcome, List<string> reasons)
    {
        lock (_rejectedLock)
        {
            var list = _rejected.GetOrAdd(userId, _ => new List<RejectedBet>());
            list.Add(new RejectedBet
            {
                MatchId   = matchId,
                Team      = team,
                Outcome   = outcome,
                Reasons   = reasons,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public List<RejectedBet> GetRejected(int userId)
    {
        lock (_rejectedLock)
        {
            return _rejected.TryGetValue(userId, out var list)
                ? list.OrderByDescending(r => r.Timestamp).ToList()
                : new List<RejectedBet>();
        }
    }

    // ── Computed stats used by ValidationService ───────────────────────────────

    public async Task<decimal> GetTotalExposureAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetTotalExposureAsync(userId);
    }

    public async Task<int> CountBetsOnMatchAsync(int userId, string matchId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.CountBetsOnMatchAsync(matchId, userId);
    }

    public async Task<int> GetConsecutiveLossesAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetConsecutiveLossesAsync(userId);
    }

    public async Task<int> GetCurrentStreakAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetCurrentStreakAsync(userId);
    }

    // ── Aggregate stats used by BettingController ─────────────────────────────

    public async Task<(int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV)> GetStatsAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var (total, wins, losses, pnl, avgClv) = await repo.GetSettledStatsAsync(userId);
        return (total, wins, losses, pnl, avgClv > 0 ? avgClv : (double?)null);
    }

    public async Task<decimal> GetTotalStakedAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetTotalStakedAsync(userId);
    }

    public async Task<double?> GetAverageEdgeAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var avg  = await repo.GetAverageEdgeAsync(userId);
        return avg.HasValue ? Math.Round(avg.Value * 100, 2) : null;
    }

    public async Task<List<object>> GetStatsBySportAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo   = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var slices = await repo.GetSettledSlicesAsync(userId);

        return slices
            .GroupBy(b => b.SportType.ToString())
            .Select(g => (object)new
            {
                Sport    = g.Key,
                Total    = g.Count(),
                Wins     = g.Count(b => b.Result == "Win"),
                Losses   = g.Count(b => b.Result == "Loss"),
                WinRate  = g.Count() > 0 ? Math.Round((double)g.Count(b => b.Result == "Win") / g.Count() * 100, 1) : 0,
                TotalPnL = g.Sum(b => b.PnL),
                AvgEdge  = g.Count() > 0 ? Math.Round(g.Average(b => b.Edge) * 100, 1) : 0,
                AvgCLV   = g.Any(b => b.CLV.HasValue)
                    ? Math.Round(g.Where(b => b.CLV.HasValue).Average(b => b.CLV!.Value), 2)
                    : (double?)null,
            })
            .ToList();
    }

    public async Task<List<object>> GetCalibrationReportAsync(int userId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo   = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var slices = await repo.GetSettledSlicesAsync(userId);

        return slices
            // Decile buckets: 0.95 clamps a 100%-predicted bet into the 90-100% bucket
            // instead of a lone 100-110% bucket.
            .GroupBy(s => (int)Math.Min(Math.Floor(s.Probability * 10), 9))
            .OrderBy(g => g.Key)
            .Select(g => (object)new
            {
                Bucket             = $"{g.Key * 10}-{g.Key * 10 + 10}%",
                SampleSize         = g.Count(),
                PredictedAvgPct    = Math.Round(g.Average(s => s.Probability) * 100, 1),
                ActualWinRatePct   = Math.Round((double)g.Count(s => s.Result == "Win") / g.Count() * 100, 1),
                GapPct             = Math.Round(
                    ((double)g.Count(s => s.Result == "Win") / g.Count() - g.Average(s => s.Probability)) * 100, 1),
            })
            .ToList();
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static Bet MapToEntity(BetHistory d, int userId) => new()
    {
        Id = d.Id, UserId = userId, MatchId = d.MatchId,
        HomeTeam = d.HomeTeam, AwayTeam = d.AwayTeam, Team = d.Team,
        Outcome = d.Outcome, Odds = d.Odds, ClosingOdds = d.ClosingOdds,
        CLV = d.CLV, Probability = d.Probability, Edge = d.Edge,
        Stake = d.Stake, DateTimePlaced = d.DateTimePlaced,
        LineMovementStatus = d.LineMovementStatus, Result = d.Result,
        PnL = d.PnL, SportType = d.SportType
    };

    private static BetHistory MapToDomain(Bet e) => new()
    {
        Id = e.Id, MatchId = e.MatchId, HomeTeam = e.HomeTeam,
        AwayTeam = e.AwayTeam, Team = e.Team, Outcome = e.Outcome,
        Odds = e.Odds, ClosingOdds = e.ClosingOdds, CLV = e.CLV,
        Probability = e.Probability, Edge = e.Edge, Stake = e.Stake,
        DateTimePlaced = e.DateTimePlaced, LineMovementStatus = e.LineMovementStatus,
        Result = e.Result, PnL = e.PnL, SportType = e.SportType
    };
}

public class RejectedBet
{
    public string       MatchId   { get; set; } = string.Empty;
    public string       Team      { get; set; } = string.Empty;
    public string       Outcome   { get; set; } = string.Empty;
    public List<string> Reasons   { get; set; } = new();
    public DateTime     Timestamp { get; set; }
}
