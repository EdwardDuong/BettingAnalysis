using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Central validation gate that enforces ALL professional betting rules before allowing a bet.
/// This is the last line of defence before money is committed.
///
/// Rules enforced (in order):
///   1.  Timing window      — 1–6 hours before kickoff only
///   2.  Edge threshold     — edge ≥ EdgeThreshold
///   3.  High edge warning  — flag if edge ≥ HighEdgeThreshold (≥ 20%)
///   4.  Line movement      — block if odds drifting against selection
///   5.  Stop-loss          — system halted if cumulative drawdown ≥ limit
///   6.  Daily loss limit   — no more bets today if daily loss ≥ limit
///   7.  Max stake cap      — stake cannot exceed MaxStakePerBet
///   8.  Exposure limit     — total open exposure must stay ≤ 10% bankroll
///   9.  Max bets per match — correlation limit (default: 2)
///   10. Tilt protection    — halt after N consecutive losses (default: 3)
///   11. Team blacklist     — emotional bias protection
/// </summary>
public class ValidationService
{
    private readonly BettingConfigService    _cfg;
    private readonly BankrollService         _bankroll;
    private readonly BettingLoggingService   _log;
    private readonly LineMovementService     _lineMovement;

    public ValidationService(
        BettingConfigService  cfg,
        BankrollService       bankroll,
        BettingLoggingService log,
        LineMovementService   lineMovement)
    {
        _cfg          = cfg;
        _bankroll     = bankroll;
        _log          = log;
        _lineMovement = lineMovement;
    }

    public ValidationResult Validate(
        MatchOdds      match,
        string         team,
        decimal        odds,
        double         edge,
        decimal        stake,
        LineMovement   lineMovement)
    {
        var result   = new ValidationResult();
        var config   = _cfg.Get();
        var bankroll = _bankroll.GetBankroll();
        var now      = DateTime.UtcNow;
        var hoursUntil = (match.MatchStartTime - now).TotalHours;

        // ── 1. Timing window (1–6 hours) ──────────────────────────────────────
        if (hoursUntil < config.PreMatchMinHours)
            result.Fail($"Kickoff in {hoursUntil:F1}h — too close (minimum {config.PreMatchMinHours}h).");

        if (hoursUntil > config.PreMatchMaxHours)
            result.Fail($"Kickoff in {hoursUntil:F1}h — too far (maximum betting window is {config.PreMatchMaxHours}h).");

        // ── 2. Edge threshold ──────────────────────────────────────────────────
        if (edge < config.EdgeThreshold)
            result.Fail($"Edge {edge:P1} is below the {config.EdgeThreshold:P0} threshold.");

        // ── 3. High edge — warn but don't block ────────────────────────────────
        if (edge >= config.HighEdgeThreshold)
            result.Warn($"Edge {edge:P1} exceeds {config.HighEdgeThreshold:P0} — manually verify Poisson inputs.");

        // ── 4. Line movement ───────────────────────────────────────────────────
        if (config.RequireLineMovementCheck && _lineMovement.ShouldBlock(lineMovement))
            result.Fail("Bet blocked: odds drifting against prediction (market disagrees with your model).");

        // ── 5. System stop-loss ────────────────────────────────────────────────
        if (bankroll.IsStopLossTriggered)
            result.Fail($"SYSTEM HALTED — cumulative loss ${bankroll.CumulativeLoss:N2} ≥ stop-loss ${bankroll.StopLossLimit:N2}.");

        // ── 6. Daily loss limit ────────────────────────────────────────────────
        if (bankroll.IsDailyLimitReached)
            result.Fail($"Daily loss limit reached: ${bankroll.DailyLossUsed:N2} of ${bankroll.DailyLossLimit:N2}.");

        // ── 7. Max stake per bet ───────────────────────────────────────────────
        if (stake > bankroll.MaxStakePerBet)
            result.Fail($"Stake ${stake:N2} exceeds max-per-bet ${bankroll.MaxStakePerBet:N2}.");

        // ── 8. Total exposure ≤ 10% bankroll ──────────────────────────────────
        var exposure    = _log.GetTotalExposure();
        var maxExposure = bankroll.TotalBankroll * (decimal)config.MaxExposurePercent;
        if (exposure + stake > maxExposure)
            result.Fail($"Exposure limit: adding ${stake:N2} would bring total to ${exposure + stake:N2} " +
                        $"(limit ${maxExposure:N2} = {config.MaxExposurePercent:P0}).");

        // ── 9. Max bets per match (correlation) ───────────────────────────────
        var betsOnMatch = _log.CountBetsOnMatch(match.MatchId);
        if (betsOnMatch >= config.MaxBetsPerMatch)
            result.Fail($"Already {betsOnMatch} bet(s) on this match (max {config.MaxBetsPerMatch}).");

        // ── 10. Tilt protection ────────────────────────────────────────────────
        var consecutiveLosses = _log.GetConsecutiveLosses();
        if (consecutiveLosses >= config.MaxConsecutiveLosses)
            result.Fail($"Tilt protection active: {consecutiveLosses} consecutive losses. Review strategy before resuming.");

        // ── 11. Team blacklist ─────────────────────────────────────────────────
        if (config.TeamBlacklist.Any(t => t.Equals(team, StringComparison.OrdinalIgnoreCase)))
            result.Fail($"{team} is on the team blacklist (emotional bias protection).");

        // ── Soft pre-warnings ──────────────────────────────────────────────────
        if (consecutiveLosses == config.MaxConsecutiveLosses - 1)
            result.Warn($"One more loss triggers tilt protection ({consecutiveLosses}/{config.MaxConsecutiveLosses} consecutive losses).");

        if (exposure + stake > maxExposure * 0.80m)
            result.Warn($"Exposure at {((exposure + stake) / maxExposure):P0} of limit.");

        return result;
    }
}
