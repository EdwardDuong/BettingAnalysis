using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

public interface IBettingLoggingService
{
    // ── Bet History ───────────────────────────────────────────────────────────
    Task LogBetAsync(int userId, BetHistory bet);
    Task<List<BetHistory>> GetHistoryAsync(int userId);
    Task<(List<BetHistory> Items, int Total)> GetHistoryPagedAsync(int userId, int page, int pageSize);

    /// <summary>Returns null if the bet doesn't exist or doesn't belong to <paramref name="userId"/>.</summary>
    Task<BetHistory?> GetByIdAsync(Guid id, int userId);

    /// <summary>No-ops if the bet doesn't exist or doesn't belong to <paramref name="userId"/>.</summary>
    Task UpdateResultAsync(Guid id, int userId, string result, decimal pnl, decimal? closingOdds, double? clv);

    // ── Rejected Bets ─────────────────────────────────────────────────────────
    void LogRejected(int userId, string matchId, string team, string outcome, List<string> reasons);
    List<RejectedBet> GetRejected(int userId);

    // ── Computed Stats (used by ValidationService) ───────────────────────────
    Task<decimal> GetTotalExposureAsync(int userId);
    Task<int> CountBetsOnMatchAsync(int userId, string matchId);
    Task<int> GetConsecutiveLossesAsync(int userId);
    Task<int> GetCurrentStreakAsync(int userId);

    // ── Aggregate Stats (used by BettingController) ──────────────────────────
    Task<(int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV)> GetStatsAsync(int userId);
    Task<decimal> GetTotalStakedAsync(int userId);
    Task<double?> GetAverageEdgeAsync(int userId);
    Task<List<object>> GetStatsBySportAsync(int userId);
}
