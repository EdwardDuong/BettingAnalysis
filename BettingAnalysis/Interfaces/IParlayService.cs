using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

public interface IParlayService
{
    Task<List<ParlayCombo>> BuildCombosAsync(List<BetOpportunity> opportunities, Bankroll bankroll);

    /// <summary>
    /// Finds the safest way to reach BettingConfig.DailyDoubleTargetOdds (default 2x)
    /// combined odds from today's eligible opportunities — a single leg if one alone
    /// clears the target and that happens to be safer than any combination, otherwise
    /// a greedily-built multi-leg combo (up to DailyDoubleMaxLegs), stopping the
    /// instant the target is cleared. Returns null if nothing available today can
    /// safely clear the target.
    /// </summary>
    Task<ParlayCombo?> BuildDailyDoubleAsync(List<BetOpportunity> opportunities, Bankroll bankroll);
}
