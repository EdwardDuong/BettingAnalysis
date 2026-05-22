using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Audit log service for all bet activity (placed, pending, settled, rejected).
/// Persists to JSON file for restart survival.
/// </summary>
public interface IBettingLoggingService
{
    // ── Bet History ───────────────────────────────────────────────────────────
    void LogBet(BetHistory bet);
    List<BetHistory> GetHistory();
    BetHistory? GetById(Guid id);
    void UpdateResult(Guid id, string result, decimal pnl, decimal? closingOdds, double? clv);

    // ── Rejected Bets ─────────────────────────────────────────────────────────
    void LogRejected(string matchId, string team, string outcome, List<string> reasons);
    List<RejectedBet> GetRejected();

    // ── Computed Stats (used by ValidationService) ───────────────────────────
    decimal GetTotalExposure();
    int CountBetsOnMatch(string matchId);
    int GetConsecutiveLosses();
    int GetCurrentStreak();
}
