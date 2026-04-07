using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public enum LineMovement { Stable, Steaming, Drifting }

/// <summary>
/// Line Movement Detection Service.
///
/// Professional Rule: If the market is moving AGAINST your selection
/// (odds drifting / lengthening), sharp money is on the other side.
/// Do not bet into a drift — the market knows something you don't.
///
/// Steaming = odds shortening = market backing your selection = ✅ positive signal
/// Drifting  = odds lengthening = market against your selection = ❌ block the bet
///
/// A move is only "significant" when the implied probability shifts by ≥ 3%.
/// Smaller moves are noise and classed as Stable.
/// </summary>
public class LineMovementService
{
    /// <summary>Minimum implied probability shift to classify as a significant move.</summary>
    private const double SignificantMovePct = 0.03;

    /// <summary>Analyse line movement for a single outcome.</summary>
    public LineMovement GetMovement(decimal currentOdds, decimal? previousOdds)
    {
        if (!previousOdds.HasValue || previousOdds.Value <= 0) return LineMovement.Stable;

        double prevImplied = 1.0 / (double)previousOdds.Value;
        double currImplied = 1.0 / (double)currentOdds;
        double delta = currImplied - prevImplied;   // positive = implied prob rose = odds shortened

        if (Math.Abs(delta) < SignificantMovePct) return LineMovement.Stable;
        return delta > 0 ? LineMovement.Steaming : LineMovement.Drifting;
    }

    public string GetLabel(LineMovement m) => m switch
    {
        LineMovement.Steaming => "↓ Steaming — market backing this selection",
        LineMovement.Drifting => "↑ Drifting — market moving against this selection",
        _                     => "→ Stable"
    };

    /// <summary>True when the rule requires blocking the bet.</summary>
    public bool ShouldBlock(LineMovement m) => m == LineMovement.Drifting;
}
