using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Audit log for all bet activity — placed, pending, settled, and rejected.
/// Also provides computed stats used by ValidationService and BankrollPanel.
///
/// In production: replace with EF Core + SQL for persistence across restarts.
/// </summary>
public class BettingLoggingService
{
    private readonly List<BetHistory>    _history  = new();
    private readonly List<RejectedBet>   _rejected = new();
    private readonly object              _lock     = new();

    // ── Placed bets ───────────────────────────────────────────────────────────

    public void LogBet(BetHistory bet)
    {
        lock (_lock) { _history.Add(bet); }
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
            bet.Result       = result;
            bet.PnL          = pnl;
            bet.ClosingOdds  = closingOdds;
            bet.CLV          = clv;
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

    /// <summary>Sum of stakes on all Pending bets (current open exposure).</summary>
    public decimal GetTotalExposure()
    {
        lock (_lock) { return _history.Where(b => b.Result == "Pending").Sum(b => b.Stake); }
    }

    /// <summary>Count of pending bets on a specific match (correlation check).</summary>
    public int CountBetsOnMatch(string matchId)
    {
        lock (_lock) { return _history.Count(b => b.MatchId == matchId && b.Result == "Pending"); }
    }

    /// <summary>
    /// Count consecutive losses at the tail of settled bet history.
    /// Pending bets are ignored (not yet resolved).
    /// </summary>
    public int GetConsecutiveLosses()
    {
        lock (_lock)
        {
            var settled = _history
                .Where(b => b.Result is "Win" or "Loss")
                .OrderByDescending(b => b.DateTimePlaced)
                .ToList();

            int count = 0;
            foreach (var bet in settled)
            {
                if (bet.Result == "Loss") count++;
                else break;
            }
            return count;
        }
    }

    /// <summary>Aggregate stats for the dashboard summary bar.</summary>
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
}

/// <summary>Record of a bet that was blocked by ValidationService.</summary>
public class RejectedBet
{
    public string MatchId   { get; set; } = string.Empty;
    public string Team      { get; set; } = string.Empty;
    public string Outcome   { get; set; } = string.Empty;
    public List<string> Reasons { get; set; } = new();
    public DateTime Timestamp   { get; set; }
}
