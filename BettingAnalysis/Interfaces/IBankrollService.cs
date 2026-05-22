using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for managing bankroll state (money in/out, daily counters).
/// Thread-safe for concurrent access.
/// </summary>
public interface IBankrollService
{
    /// <summary>Gets a snapshot of current bankroll state.</summary>
    Bankroll GetBankroll();

    /// <summary>Reserves stake from available bankroll (money locked for pending bet).</summary>
    void ReserveStake(decimal stake);

    /// <summary>Updates bankroll after bet result (Win/Loss).</summary>
    void UpdateAfterResult(decimal stake, decimal odds, string result);

    /// <summary>Resets bankroll to initial or specified amount. Clears all loss counters.</summary>
    void Reset(decimal? newAmount = null);
}
