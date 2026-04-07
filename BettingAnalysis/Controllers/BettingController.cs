using BettingAnalysis.Models;
using BettingAnalysis.Services;
using Microsoft.AspNetCore.Mvc;

namespace BettingAnalysis.Controllers;

/// <summary>
/// Main Betting API controller.
///
/// Endpoints:
///   GET  /Betting/opportunities  — Pre-match value bets (Edge >= threshold)
///   POST /Betting/place          — Simulate placing a bet with full risk checks
///   GET  /Betting/history        — All placed bets and their results
///   GET  /Betting/bankroll       — Current bankroll state
///   POST /Betting/result/{id}    — Mark a pending bet as Win or Loss
/// </summary>
[ApiController]
[Route("[controller]")]
public class BettingController : ControllerBase
{
    private readonly OddsService          _oddsService;
    private readonly PoissonService       _poissonService;
    private readonly EdgeService          _edgeService;
    private readonly BetSizingService     _betSizingService;
    private readonly BankrollService      _bankrollService;
    private readonly BettingLoggingService _loggingService;
    private readonly double               _edgeThreshold;

    public BettingController(
        OddsService          oddsService,
        PoissonService       poissonService,
        EdgeService          edgeService,
        BetSizingService     betSizingService,
        BankrollService      bankrollService,
        BettingLoggingService loggingService,
        IConfiguration       config)
    {
        _oddsService      = oddsService;
        _poissonService   = poissonService;
        _edgeService      = edgeService;
        _betSizingService = betSizingService;
        _bankrollService  = bankrollService;
        _loggingService   = loggingService;
        // Rule #2: Minimum edge to show an opportunity (default 5%)
        _edgeThreshold    = config.GetValue<double>("BettingSettings:EdgeThreshold", 0.05);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/opportunities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all pre-match value bet opportunities.
    ///
    /// Pipeline:
    ///   1. OddsService filters to pre-match only (Rule #1)
    ///   2. PoissonService generates outcome probabilities
    ///   3. EdgeService calculates model edge vs bookmaker implied probability
    ///   4. Filter by EdgeThreshold >= 5% (Rule #2)
    ///   5. BetSizingService calculates Kelly stake (Rule #3)
    ///   6. Sort by edge descending (best value first)
    ///
    /// Returns empty list if stop-loss is triggered (Rule #5).
    /// </summary>
    [HttpGet("opportunities")]
    public ActionResult<List<BetOpportunity>> GetOpportunities()
    {
        var bankroll = _bankrollService.GetBankroll();

        // Rule #5: System halted — no opportunities served
        if (bankroll.IsStopLossTriggered)
            return Ok(new List<BetOpportunity>());

        var preMatchOdds  = _oddsService.GetPreMatchOdds();   // Rule #1 already applied
        var opportunities = new List<BetOpportunity>();

        foreach (var match in preMatchOdds)
        {
            var prediction = _poissonService.Predict(match);

            // Build candidate outcomes for this match
            var candidates = new List<(string Outcome, string Team, decimal Odds, double Prob)>
            {
                ("Home", match.HomeTeam, match.HomeOdds, prediction.HomeWinProb),
                ("Away", match.AwayTeam, match.AwayOdds, prediction.AwayWinProb)
            };

            // Draw market only for EPL (Rule: sport-appropriate markets)
            if (match.DrawOdds.HasValue && match.SportType == SportType.EPL)
                candidates.Add(("Draw", "Draw", match.DrawOdds.Value, prediction.DrawProb));

            foreach (var (outcome, team, odds, prob) in candidates)
            {
                var edge = _edgeService.CalculateEdge(prob, odds);

                // Rule #2: Skip if edge below threshold
                if (edge < _edgeThreshold) continue;

                var stake = _betSizingService.CalculateStake(prob, odds, bankroll.AvailableBankroll);

                // Cap suggested stake at MaxStakePerBet (Rule #3)
                stake = Math.Min(stake, bankroll.MaxStakePerBet);

                opportunities.Add(new BetOpportunity
                {
                    MatchId       = match.MatchId,
                    HomeTeam      = match.HomeTeam,
                    AwayTeam      = match.AwayTeam,
                    Team          = team,
                    Outcome       = outcome,
                    Odds          = odds,
                    Probability   = Math.Round(prob, 4),
                    Edge          = Math.Round(edge, 4),
                    SuggestedStake = stake,
                    SportType     = match.SportType,
                    MatchStartTime = match.MatchStartTime
                });
            }
        }

        // Best opportunities first (highest edge)
        return Ok(opportunities.OrderByDescending(o => o.Edge));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/place
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulate placing a bet with full risk management validation.
    ///
    /// Validation order:
    ///   1. Match must be pre-match (Rule #1)
    ///   2. Edge must meet threshold (Rule #2)
    ///   3. Stake capped at MaxStakePerBet (Rule #3)
    ///   4. Daily loss limit check (Rule #4)
    ///   5. Stop-loss check (Rule #5)
    ///   6. Rule #8: warn if edge > 20% (flag RequiresVerification)
    ///   7. Rule #9: log the bet
    ///   8. Rule #10: reserve stake in bankroll
    /// </summary>
    [HttpPost("place")]
    public ActionResult<BetHistory> PlaceBet([FromBody] PlaceBetRequest request)
    {
        // Rule #1: Match must still be pre-match
        var preMatchOdds = _oddsService.GetPreMatchOdds();
        var match = preMatchOdds.FirstOrDefault(m => m.MatchId == request.MatchId);
        if (match is null)
            return NotFound($"Match '{request.MatchId}' not found or has passed the pre-match cutoff.");

        var prediction = _poissonService.Predict(match);

        // Resolve outcome fields
        (decimal odds, double prob, string team) = request.Outcome switch
        {
            "Home" => (match.HomeOdds, prediction.HomeWinProb, match.HomeTeam),
            "Away" => (match.AwayOdds, prediction.AwayWinProb, match.AwayTeam),
            "Draw" => match.DrawOdds.HasValue
                ? (match.DrawOdds.Value, prediction.DrawProb, "Draw")
                : throw new InvalidOperationException(),
            _ => throw new InvalidOperationException()
        };

        if (request.Outcome == "Draw" && !match.DrawOdds.HasValue)
            return BadRequest("No draw market for this sport.");

        var edge = _edgeService.CalculateEdge(prob, odds);

        // Rule #2: Edge check
        if (edge < _edgeThreshold)
            return BadRequest(
                $"Edge {edge:P2} is below the minimum threshold of {_edgeThreshold:P0}. Bet rejected.");

        // Rule #8: High edge warning (>20%) — would normally require second confirmation
        bool requiresVerification = edge >= 0.20;

        var bankroll = _bankrollService.GetBankroll();

        // Determine stake: custom or Kelly-calculated, capped at MaxStakePerBet (Rule #3)
        decimal stake = request.CustomStake
            ?? _betSizingService.CalculateStake(prob, odds, bankroll.AvailableBankroll);
        stake = Math.Min(stake, bankroll.MaxStakePerBet);

        // Rules #3, #4, #5: Bankroll gate
        var (allowed, reason) = _bankrollService.CanPlaceBet(stake);
        if (!allowed)
            return BadRequest(reason);

        // Rule #10: Reserve the stake
        _bankrollService.ReserveStake(stake);

        var bet = new BetHistory
        {
            MatchId        = match.MatchId,
            HomeTeam       = match.HomeTeam,
            AwayTeam       = match.AwayTeam,
            Team           = team,
            Outcome        = request.Outcome,
            Odds           = odds,
            Probability    = Math.Round(prob, 4),
            Edge           = Math.Round(edge, 4),
            Stake          = stake,
            DateTimePlaced = DateTime.UtcNow,
            Result         = "Pending",
            SportType      = match.SportType
        };

        // Rule #9: Log every bet
        _loggingService.LogBet(bet);

        return Ok(new
        {
            Bet = bet,
            Warning = requiresVerification
                ? "Edge exceeds 20% — please manually verify the Poisson inputs before confirming."
                : null
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/history
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("history")]
    public ActionResult<List<BetHistory>> GetHistory()
        => Ok(_loggingService.GetHistory());

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/bankroll
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("bankroll")]
    public ActionResult<Bankroll> GetBankroll()
        => Ok(_bankrollService.GetBankroll());

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/result/{id}   body: "Win" or "Loss"
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mark a pending bet as Win or Loss and update the bankroll.
    /// Rule #10: Bankroll must be updated after every result.
    /// </summary>
    [HttpPost("result/{id}")]
    public ActionResult UpdateResult(Guid id, [FromBody] string result)
    {
        if (result is not ("Win" or "Loss"))
            return BadRequest("Result must be \"Win\" or \"Loss\".");

        var bet = _loggingService.GetById(id);
        if (bet is null)           return NotFound($"Bet {id} not found.");
        if (bet.Result != "Pending") return BadRequest("Result has already been recorded.");

        decimal pnl = result == "Win"
            ? bet.Stake * (bet.Odds - 1m)   // Profit on win
            : -bet.Stake;                    // Full stake lost

        _loggingService.UpdateResult(id, result, pnl);
        _bankrollService.UpdateAfterResult(bet.Stake, bet.Odds, result);

        return Ok(new { BetId = id, Result = result, PnL = pnl });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public ActionResult GetStats()
    {
        var (total, wins, losses, totalPnL) = _loggingService.GetStats();
        double winRate = total > 0 ? (double)wins / total : 0;
        return Ok(new { Total = total, Wins = wins, Losses = losses, WinRate = winRate, TotalPnL = totalPnL });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/refresh
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Force-clear the odds cache so the next /opportunities call re-fetches from the API.
    /// Rule #7: call this every 30–60 minutes in production.
    /// </summary>
    [HttpPost("refresh")]
    public ActionResult RefreshOdds()
    {
        _oddsService.InvalidateCache();
        return Ok(new { Message = "Odds cache cleared. Next request will fetch fresh data." });
    }
}
