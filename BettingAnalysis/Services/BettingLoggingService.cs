using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Audit log for all bet activity — placed, pending, settled, and rejected.
/// Now persisted to SQL Server database for production-grade reliability.
/// Uses repository pattern for data access with async operations.
/// </summary>
public class BettingLoggingService : IBettingLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<RejectedBet>    _rejected = new(); // In-memory for now
    private readonly object               _lock     = new();
    private const int DefaultUserId = 1; // Individual use — default user

    public BettingLoggingService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    // ── Placed bets ───────────────────────────────────────────────────────────

    public void LogBet(BetHistory bet)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var entity = MapToEntity(bet);
        repo.AddAsync(entity).GetAwaiter().GetResult();
    }

    public List<BetHistory> GetHistory()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var bets = repo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult();
        return bets.Select(MapToDomain).ToList();
    }

    public BetHistory? GetById(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var bet = repo.GetByIdAsync(id).GetAwaiter().GetResult();
        return bet != null ? MapToDomain(bet) : null;
    }

    public void UpdateResult(Guid id, string result, decimal pnl, decimal? closingOdds, double? clv)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var bet = repo.GetByIdAsync(id).GetAwaiter().GetResult();
        if (bet is null) return;

        bet.Result = result;
        bet.PnL = pnl;
        bet.ClosingOdds = closingOdds;
        bet.CLV = clv;

        repo.UpdateAsync(bet).GetAwaiter().GetResult();
    }

    // ── Rejected bets (logged for analysis) ───────────────────────────────────

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

    public decimal GetTotalExposure()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return repo.GetTotalExposureAsync(DefaultUserId).GetAwaiter().GetResult();
    }

    public int CountBetsOnMatch(string matchId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return repo.CountBetsOnMatchAsync(matchId, DefaultUserId).GetAwaiter().GetResult();
    }

    public int GetConsecutiveLosses()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return repo.GetConsecutiveLossesAsync(DefaultUserId).GetAwaiter().GetResult();
    }

    public int GetCurrentStreak()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();
        return repo.GetCurrentStreakAsync(DefaultUserId).GetAwaiter().GetResult();
    }

    public List<object> GetStatsBySport()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var bets = repo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult()
            .Where(b => b.Result != "Pending");

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

    public decimal GetTotalStaked()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var bets = repo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult();
        return bets.Where(b => b.Result != "Pending").Sum(b => b.Stake);
    }

    public double? GetAverageEdge()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var settled = repo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult()
            .Where(b => b.Result != "Pending")
            .ToList();

        return settled.Count > 0
            ? Math.Round(settled.Average(b => b.Edge) * 100, 2)
            : null;
    }

    public (int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV) GetStats()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBetRepository>();

        var settled = repo.GetAllAsync(DefaultUserId).GetAwaiter().GetResult()
            .Where(b => b.Result != "Pending")
            .ToList();

        var clvBets = settled.Where(b => b.CLV.HasValue).ToList();
        double? avgCLV = clvBets.Count > 0 ? clvBets.Average(b => b.CLV!.Value) : null;

        return (settled.Count, settled.Count(b => b.Result == "Win"),
                settled.Count(b => b.Result == "Loss"), settled.Sum(b => b.PnL), avgCLV);
    }

    // ── Mapping between domain model and entity ───────────────────────────────

    private static Bet MapToEntity(BetHistory domain) => new()
    {
        Id = domain.Id,
        UserId = DefaultUserId,
        MatchId = domain.MatchId,
        HomeTeam = domain.HomeTeam,
        AwayTeam = domain.AwayTeam,
        Team = domain.Team,
        Outcome = domain.Outcome,
        Odds = domain.Odds,
        ClosingOdds = domain.ClosingOdds,
        CLV = domain.CLV,
        Probability = domain.Probability,
        Edge = domain.Edge,
        Stake = domain.Stake,
        DateTimePlaced = domain.DateTimePlaced,
        LineMovementStatus = domain.LineMovementStatus,
        Result = domain.Result,
        PnL = domain.PnL,
        SportType = domain.SportType
    };

    private static BetHistory MapToDomain(Bet entity) => new()
    {
        Id = entity.Id,
        MatchId = entity.MatchId,
        HomeTeam = entity.HomeTeam,
        AwayTeam = entity.AwayTeam,
        Team = entity.Team,
        Outcome = entity.Outcome,
        Odds = entity.Odds,
        ClosingOdds = entity.ClosingOdds,
        CLV = entity.CLV,
        Probability = entity.Probability,
        Edge = entity.Edge,
        Stake = entity.Stake,
        DateTimePlaced = entity.DateTimePlaced,
        LineMovementStatus = entity.LineMovementStatus,
        Result = entity.Result,
        PnL = entity.PnL,
        SportType = entity.SportType
    };
}

/// <summary>Record of a bet that was blocked by ValidationService.</summary>
public class RejectedBet
{
    public string       MatchId   { get; set; } = string.Empty;
    public string       Team      { get; set; } = string.Empty;
    public string       Outcome   { get; set; } = string.Empty;
    public List<string> Reasons   { get; set; } = new();
    public DateTime     Timestamp { get; set; }
}
