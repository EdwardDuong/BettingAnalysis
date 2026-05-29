using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IBankrollService
{
    Task<Bankroll> GetBankrollAsync();
    Task ReserveStakeAsync(decimal stake);
    Task UpdateAfterResultAsync(decimal stake, decimal odds, string result);
    Task ResetAsync(decimal? newAmount = null);
}
