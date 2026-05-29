using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Interfaces;

public interface IBettingLoggingService
{
    // ── Bet History ───────────────────────────────────────────────────────────
    Task LogBetAsync(BetHistory bet);
    Task<List<BetHistory>> GetHistoryAsync();
    Task<BetHistory?> GetByIdAsync(Guid id);
    Task UpdateResultAsync(Guid id, string result, decimal pnl, decimal? closingOdds, double? clv);

    // ── Rejected Bets ─────────────────────────────────────────────────────────
    void LogRejected(string matchId, string team, string outcome, List<string> reasons);
    List<RejectedBet> GetRejected();

    // ── Computed Stats (used by ValidationService) ───────────────────────────
    Task<decimal> GetTotalExposureAsync();
    Task<int> CountBetsOnMatchAsync(string matchId);
    Task<int> GetConsecutiveLossesAsync();
    Task<int> GetCurrentStreakAsync();

    // ── Aggregate Stats (used by BettingController) ──────────────────────────
    Task<(int Total, int Wins, int Losses, decimal TotalPnL, double? AvgCLV)> GetStatsAsync();
    Task<decimal> GetTotalStakedAsync();
    Task<double?> GetAverageEdgeAsync();
    Task<List<object>> GetStatsBySportAsync();
}
