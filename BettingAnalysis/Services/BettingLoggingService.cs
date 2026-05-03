using BettingAnalysis.Models;
using System.Text.Json;

namespace BettingAnalysis.Services;

/// <summary>
/// Audit log for all bet activity — placed, pending, settled, and rejected.
/// History is persisted to a JSON file so it survives application restarts.
/// Path defaults to bet-history.json in the working directory; override via
/// BettingSettings:HistoryFilePath in appsettings.json.
/// </summary>
public class BettingLoggingService
{
    private readonly List<BetHistory>    _history  = new();
    private readonly List<RejectedBet>   _rejected = new();
    private readonly object              _lock     = new();
    private readonly string              _path;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public BettingLoggingService(IConfiguration config, ILogger<BettingLoggingService> logger)
    {
        _path = config.GetValue<string>("BettingSettings:HistoryFilePath") ?? "bet-history.json";
        Load(logger);
    }

    // ── Placed bets ───────────────────────────────────────────────────────────

    public void LogBet(BetHistory bet)
    {
        lock (_lock) { _history.Add(bet); Save(); }
    }

    public List<BetHistory> GetHistory()
    {
        lock (_lock) { return _history.OrderByDescending(b => b.DateTimePlaced).ToList(); }
    }

    public BetHistory? GetById(Guid id)
    {
        lock (_lock) { return _history.FirstOrDefault(b => b.Id == id); }
    }

    public void UpdateResult(Guid id, string result, decimal pnl, decimal? closingOdds, double? clv)
    {
        lock (_lock)
        {
            var bet = _history.FirstOrDefault(b => b.Id == id);
            if (bet is null) return;
            bet.Result      = result;
            bet.PnL         = pnl;
            bet.ClosingOdds = closingOdds;
            bet.CLV         = clv;
            Save();
        }
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
        lock (_lock) { return _history.Where(b => b.Result == "Pending").Sum(b => b.Stake); }
    }

    public int CountBetsOnMatch(string matchId)
    {
        lock (_lock) { return _history.Count(b => b.MatchId == matchId && b.Result == "Pending"); }
    }

    public int GetConsecutiveLosses()
    {
        lock (_lock)
        {
            int count = 0;
            foreach (var bet in _history
                .Where(b => b.Result is "Win" or "Loss")
                .OrderByDescending(b => b.DateTimePlaced))
            {
                if (bet.Result == "Loss") count++;
                else break;
            }
            return count;
        }
    }

    public List<object> GetStatsBySport()
    {
        lock (_lock)
        {
            return _history
                .Where(b => b.Result != "Pending")
                .GroupBy(b => b.SportType.ToString())
                .Select(g => (object)new
                {
                    Sport    = g.Key,
                    Total    = g.Count(),
                    Wins     = g.Count(b => b.Result == "Win"),
                    Losses   = g.Count(b => b.Result == "Loss"),
                    WinRate  = g.Count() > 0 ? Math.Round((double)g.Count(b => b.Result == "Win") / g.Count() * 100, 1) : 0,
                    TotalPnL = g.Sum(b => b.PnL),
                    AvgEdge  = g.Count() > 0 ? Math.Round(g.Average(b => (double)b.Edge) * 100, 1) : 0,
                    AvgCLV   = g.Any(b => b.CLV.HasValue)
                        ? Math.Round(g.Where(b => b.CLV.HasValue).Average(b => b.CLV!.Value), 2)
                        : (double?)null,
                })
                .ToList();
        }
    }

    public (int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV) GetStats()
    {
        lock (_lock)
        {
            var settled = _history.Where(b => b.Result != "Pending").ToList();
            var clvBets = settled.Where(b => b.CLV.HasValue).ToList();
            double? avgCLV = clvBets.Count > 0 ? clvBets.Average(b => b.CLV!.Value) : null;
            return (settled.Count, settled.Count(b => b.Result == "Win"),
                    settled.Count(b => b.Result == "Loss"), settled.Sum(b => b.PnL), avgCLV);
        }
    }

    // ── File persistence ──────────────────────────────────────────────────────

    private void Load(ILogger logger)
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<BetHistory>>(File.ReadAllText(_path));
            if (loaded?.Count > 0)
            {
                _history.AddRange(loaded);
                logger.LogInformation("Loaded {Count} bets from {Path}", loaded.Count, _path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not load bet history: {E}", ex.Message);
        }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_history, _json)); }
        catch { /* non-fatal — in-memory state is still correct */ }
    }
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
