using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public class BettingLoggingService : IBettingLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<RejectedBet>    _rejected = new();
    private readonly object               _lock     = new();
    private const int DefaultUserId = 1;

    public BettingLoggingService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    // ── Placed bets ───────────────────────────────────────────────────────────

    public async Task LogBetAsync(BetHistory bet)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        await repo.AddAsync(MapToEntity(bet));
    }

    public async Task<List<BetHistory>> GetHistoryAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bets = await repo.GetAllAsync(DefaultUserId);
        return bets.Select(MapToDomain).ToList();
    }

    public async Task<BetHistory?> GetByIdAsync(Guid id)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bet = await repo.GetByIdAsync(id);
        return bet is null ? null : MapToDomain(bet);
    }

    public async Task UpdateResultAsync(Guid id, string result, decimal pnl, decimal? closingOdds, double? clv)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bet  = await repo.GetByIdAsync(id);
        if (bet is null) return;

        bet.Result      = result;
        bet.PnL         = pnl;
        bet.ClosingOdds = closingOdds;
        bet.CLV         = clv;
        await repo.UpdateAsync(bet);
    }

    // ── Rejected bets ─────────────────────────────────────────────────────────

    public void LogRejected(string matchId, string team, string outcome, List<string> reasons)
    {
        lock (_lock)
        {
            _rejected.Add(new RejectedBet
            {
                MatchId   = matchId,
                Team      = team,
                Outcome   = outcome,
                Reasons   = reasons,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public List<RejectedBet> GetRejected()
    {
        lock (_lock) { return _rejected.OrderByDescending(r => r.Timestamp).ToList(); }
    }

    // ── Computed stats used by ValidationService ───────────────────────────────

    public async Task<decimal> GetTotalExposureAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetTotalExposureAsync(DefaultUserId);
    }

    public async Task<int> CountBetsOnMatchAsync(string matchId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.CountBetsOnMatchAsync(matchId, DefaultUserId);
    }

    public async Task<int> GetConsecutiveLossesAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetConsecutiveLossesAsync(DefaultUserId);
    }

    public async Task<int> GetCurrentStreakAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return await repo.GetCurrentStreakAsync(DefaultUserId);
    }

    // ── Aggregate stats used by BettingController ─────────────────────────────

    public async Task<(int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV)> GetStatsAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo    = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var settled = (await repo.GetAllAsync(DefaultUserId)).Where(b => b.Result != "Pending").ToList();

        var clvBets = settled.Where(b => b.CLV.HasValue).ToList();
        double? avgCLV = clvBets.Count > 0 ? clvBets.Average(b => b.CLV!.Value) : null;

        return (settled.Count, settled.Count(b => b.Result == "Win"),
                settled.Count(b => b.Result == "Loss"), settled.Sum(b => b.PnL), avgCLV);
    }

    public async Task<decimal> GetTotalStakedAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bets = await repo.GetAllAsync(DefaultUserId);
        return bets.Where(b => b.Result != "Pending").Sum(b => b.Stake);
    }

    public async Task<double?> GetAverageEdgeAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo    = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var settled = (await repo.GetAllAsync(DefaultUserId)).Where(b => b.Result != "Pending").ToList();
        return settled.Count > 0 ? Math.Round(settled.Average(b => b.Edge) * 100, 2) : null;
    }

    public async Task<List<object>> GetStatsBySportAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        var bets = (await repo.GetAllAsync(DefaultUserId)).Where(b => b.Result != "Pending");

        return bets
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

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static Bet MapToEntity(BetHistory d) => new()
    {
        Id = d.Id, UserId = DefaultUserId, MatchId = d.MatchId,
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
