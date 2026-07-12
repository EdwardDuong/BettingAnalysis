using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IBankrollService
{
    Task<Bankroll> GetBankrollAsync(int userId);
    Task ReserveStakeAsync(int userId, decimal stake);
    Task UpdateAfterResultAsync(int userId, decimal stake, decimal odds, string result);
    Task ResetAsync(int userId, decimal? newAmount = null);
}
