using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public class ValidationService : IValidationService
{
    private readonly IBettingConfigService    _cfg;
    private readonly IBankrollService         _bankroll;
    private readonly IBettingLoggingService   _log;
    private readonly ILineMovementService     _lineMovement;

    public ValidationService(
        IBettingConfigService  cfg,
        IBankrollService       bankroll,
        IBettingLoggingService log,
        ILineMovementService   lineMovement)
    {
        _cfg          = cfg;
        _bankroll     = bankroll;
        _log          = log;
        _lineMovement = lineMovement;
    }

    public async Task<ValidationResult> ValidateAsync(
        MatchOdds    match,
        string       team,
        decimal      odds,
        double       edge,
        decimal      stake,
        LineMovement lineMovement)
    {
        var result   = new ValidationResult();
        var config   = _cfg.Get();
        var now      = DateTime.UtcNow;
        var hoursUntil = (match.MatchStartTime - now).TotalHours;

        var bankroll         = await _bankroll.GetBankrollAsync();
        var exposure         = await _log.GetTotalExposureAsync();
        var betsOnMatch      = await _log.CountBetsOnMatchAsync(match.MatchId);
        var consecutiveLosses = await _log.GetConsecutiveLossesAsync();

        // ── 1. Timing window ──────────────────────────────────────────────────
        if (hoursUntil < config.PreMatchMinHours)
            result.Fail($"Kickoff in {hoursUntil:F1}h — too close (minimum {config.PreMatchMinHours}h).");
        if (hoursUntil > config.PreMatchMaxHours)
            result.Fail($"Kickoff in {hoursUntil:F1}h — too far (maximum betting window is {config.PreMatchMaxHours}h).");

        // ── 2. Edge threshold ─────────────────────────────────────────────────
        if (edge < config.EdgeThreshold)
            result.Fail($"Edge {edge:P1} is below the {config.EdgeThreshold:P0} threshold.");

        // ── 3. High edge — warn only ──────────────────────────────────────────
        if (edge >= config.HighEdgeThreshold)
            result.Warn($"Edge {edge:P1} exceeds {config.HighEdgeThreshold:P0} — manually verify Poisson inputs.");

        // ── 4. Line movement ──────────────────────────────────────────────────
        if (config.RequireLineMovementCheck && _lineMovement.ShouldBlock(lineMovement))
            result.Fail("Bet blocked: odds drifting against prediction (market disagrees with your model).");

        // ── 5. System stop-loss ───────────────────────────────────────────────
        if (bankroll.IsStopLossTriggered)
            result.Fail($"SYSTEM HALTED — cumulative loss ${bankroll.CumulativeLoss:N2} ≥ stop-loss ${bankroll.StopLossLimit:N2}.");

        // ── 6. Daily loss limit ───────────────────────────────────────────────
        if (bankroll.IsDailyLimitReached)
            result.Fail($"Daily loss limit reached: ${bankroll.DailyLossUsed:N2} of ${bankroll.DailyLossLimit:N2}.");

        // ── 7. Max stake per bet ──────────────────────────────────────────────
        if (stake > bankroll.MaxStakePerBet)
            result.Fail($"Stake ${stake:N2} exceeds max-per-bet ${bankroll.MaxStakePerBet:N2}.");

        // ── 8. Total exposure ≤ max% bankroll ─────────────────────────────────
        var maxExposure = bankroll.TotalBankroll * (decimal)config.MaxExposurePercent;
        if (exposure + stake > maxExposure)
            result.Fail($"Exposure limit: adding ${stake:N2} would bring total to ${exposure + stake:N2} " +
                        $"(limit ${maxExposure:N2} = {config.MaxExposurePercent:P0}).");

        // ── 9. Max bets per match ─────────────────────────────────────────────
        if (betsOnMatch >= config.MaxBetsPerMatch)
            result.Fail($"Already {betsOnMatch} bet(s) on this match (max {config.MaxBetsPerMatch}).");

        // ── 10. Tilt protection ───────────────────────────────────────────────
        if (consecutiveLosses >= config.MaxConsecutiveLosses)
            result.Fail($"Tilt protection active: {consecutiveLosses} consecutive losses. Review strategy before resuming.");

        // ── 11. Team blacklist ────────────────────────────────────────────────
        if (config.TeamBlacklist.Any(t => t.Equals(team, StringComparison.OrdinalIgnoreCase)))
            result.Fail($"{team} is on the team blacklist (emotional bias protection).");

        // ── Soft warnings ─────────────────────────────────────────────────────
        if (consecutiveLosses == config.MaxConsecutiveLosses - 1)
            result.Warn($"One more loss triggers tilt protection ({consecutiveLosses}/{config.MaxConsecutiveLosses} consecutive losses).");
        if (exposure + stake > maxExposure * 0.80m)
            result.Warn($"Exposure at {((exposure + stake) / maxExposure):P0} of limit.");

        return result;
    }
}
