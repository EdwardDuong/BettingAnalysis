using BettingAnalysis.Models;
using BettingAnalysis.Services;
using Microsoft.AspNetCore.Mvc;

namespace BettingAnalysis.Controllers;

[ApiController]
[Route("[controller]")]
public class BettingController : ControllerBase
{
    private readonly OddsService          _odds;
    private readonly PoissonService       _poisson;
    private readonly EdgeService          _edge;
    private readonly BetSizingService     _sizing;
    private readonly BankrollService      _bankroll;
    private readonly BettingLoggingService _log;
    private readonly ValidationService    _validation;
    private readonly LineMovementService  _lineMovement;
    private readonly CLVService           _clv;
    private readonly BettingConfigService _cfg;
    private readonly AIValidatorService   _aiValidator;
    private readonly ParlayService        _parlay;

    public BettingController(
        OddsService           odds,
        PoissonService        poisson,
        EdgeService           edge,
        BetSizingService      sizing,
        BankrollService       bankroll,
        BettingLoggingService log,
        ValidationService     validation,
        LineMovementService   lineMovement,
        CLVService            clv,
        BettingConfigService  cfg,
        AIValidatorService    aiValidator,
        ParlayService         parlay)
    {
        _odds         = odds;
        _poisson      = poisson;
        _edge         = edge;
        _sizing       = sizing;
        _bankroll     = bankroll;
        _log          = log;
        _validation   = validation;
        _lineMovement = lineMovement;
        _clv          = clv;
        _cfg          = cfg;
        _aiValidator  = aiValidator;
        _parlay       = parlay;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/opportunities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all pre-match value bet opportunities, enriched with AI validation.
    ///
    /// Pipeline:
    ///   1.  OddsService    → matches within 1–6h betting window (Rules #1, #4)
    ///   2.  PoissonService → outcome probabilities
    ///   3.  EdgeService    → model edge vs implied probability (Rule #2)
    ///   4.  Filter         → edge ≥ EdgeThreshold (default 5%)
    ///   5.  LineMovement   → Steaming / Drifting / Stable
    ///   6.  BetSizingService → half-Kelly stake (Rule #3)
    ///   7.  AIValidatorService → Score, Decision, Flags for all opportunities
    ///   8.  Sort by AI score descending (GOOD_BET first, then RISKY)
    ///
    /// Returns empty list if stop-loss triggered (Rule #5).
    /// </summary>
    [HttpGet("opportunities")]
    public ActionResult<List<BetOpportunity>> GetOpportunities()
    {
        var bankroll = EnrichedBankroll();
        if (bankroll.IsStopLossTriggered)
            return Ok(new List<BetOpportunity>());

        var config       = _cfg.Get();
        var preMatch     = _odds.GetPreMatchOdds();
        var opportunities = new List<BetOpportunity>();
        var now          = DateTime.UtcNow;

        foreach (var match in preMatch)
        {
            var prediction = _poisson.Predict(match);

            var candidates = new List<(string Outcome, string Team, decimal Odds, decimal? PrevOdds, double Prob)>
            {
                ("Home", match.HomeTeam, match.HomeOdds, match.PreviousHomeOdds, prediction.HomeWinProb),
                ("Away", match.AwayTeam, match.AwayOdds, match.PreviousAwayOdds, prediction.AwayWinProb),
            };
            if (match.DrawOdds.HasValue && match.SportType == SportType.EPL)
                candidates.Add(("Draw", "Draw", match.DrawOdds.Value, match.PreviousDrawOdds, prediction.DrawProb));

            foreach (var (outcome, team, odds, prevOdds, prob) in candidates)
            {
                var edgeVal = _edge.CalculateEdge(prob, odds);
                if (edgeVal < config.EdgeThreshold) continue;

                var movement      = _lineMovement.GetMovement(odds, prevOdds);
                var hoursUntil    = (match.MatchStartTime - now).TotalHours;
                var stake         = _sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll);
                stake             = Math.Min(stake, bankroll.MaxStakePerBet);

                // Pre-flight validation to collect soft warnings
                var preCheck = _validation.Validate(match, team, odds, edgeVal, stake, movement);

                opportunities.Add(new BetOpportunity
                {
                    MatchId             = match.MatchId,
                    HomeTeam            = match.HomeTeam,
                    AwayTeam            = match.AwayTeam,
                    Team                = team,
                    Outcome             = outcome,
                    Odds                = odds,
                    Probability         = Math.Round(prob, 4),
                    Edge                = Math.Round(edgeVal, 4),
                    SuggestedStake      = stake,
                    SportType           = match.SportType,
                    MatchStartTime      = match.MatchStartTime,
                    HoursUntilKickoff   = Math.Round(hoursUntil, 2),
                    PreviousOdds        = prevOdds,
                    LineMovementStatus  = movement.ToString(),
                    IsHighRisk          = edgeVal >= config.HighEdgeThreshold || movement == LineMovement.Drifting,
                    RequiresManualCheck = edgeVal >= config.HighEdgeThreshold,
                    ValidationWarnings  = preCheck.Warnings,
                    ConfidenceLevel     = ComputeConfidence(prob, edgeVal),
                });
            }
        }

        // ── AI Validator ──────────────────────────────────────────────────────
        var validated = _aiValidator.Validate(opportunities);
        foreach (var opp in opportunities)
        {
            opp.AiValidation = validated.FirstOrDefault(v => v.MatchId == opp.MatchId && v.Outcome == opp.Outcome);
        }

        // Sort: GOOD_BET first, then by AI score desc, then by edge desc
        var sorted = opportunities
            .OrderBy(o => o.AiValidation?.Decision == "GOOD_BET" ? 0 : o.AiValidation?.Decision == "RISKY" ? 1 : 2)
            .ThenByDescending(o => o.AiValidation?.Score ?? 0)
            .ThenByDescending(o => o.Edge)
            .ToList();

        return Ok(sorted);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/place
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("place")]
    public ActionResult<object> PlaceBet([FromBody] PlaceBetRequest request)
    {
        var preMatch = _odds.GetPreMatchOdds();
        var match    = preMatch.FirstOrDefault(m => m.MatchId == request.MatchId);
        if (match is null)
            return NotFound($"Match '{request.MatchId}' not found or outside the betting window.");

        var prediction = _poisson.Predict(match);

        (decimal odds, double prob, string team) = request.Outcome switch
        {
            "Home" => (match.HomeOdds, prediction.HomeWinProb, match.HomeTeam),
            "Away" => (match.AwayOdds, prediction.AwayWinProb, match.AwayTeam),
            "Draw" when match.DrawOdds.HasValue
                   => (match.DrawOdds.Value, prediction.DrawProb, "Draw"),
            "Draw" => throw new InvalidOperationException("No draw market"),
            _      => throw new InvalidOperationException($"Unknown outcome: {request.Outcome}")
        };

        if (request.Outcome == "Draw" && !match.DrawOdds.HasValue)
            return BadRequest("No draw market for this sport.");

        var edgeVal  = _edge.CalculateEdge(prob, odds);
        var movement = _lineMovement.GetMovement(
            odds,
            request.Outcome == "Home" ? match.PreviousHomeOdds :
            request.Outcome == "Away" ? match.PreviousAwayOdds : match.PreviousDrawOdds);

        var bankroll = EnrichedBankroll();
        var stake    = Math.Min(
            request.CustomStake ?? _sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll),
            bankroll.MaxStakePerBet);

        // ── Full validation gate ──────────────────────────────────────────────
        var vResult = _validation.Validate(match, team, odds, edgeVal, stake, movement);

        if (!vResult.IsValid)
        {
            // Log the rejection for analysis
            _log.LogRejected(match.MatchId, team, request.Outcome, vResult.Violations);
            return BadRequest(new { Violations = vResult.Violations, Warnings = vResult.Warnings });
        }

        _bankroll.ReserveStake(stake);

        var bet = new BetHistory
        {
            MatchId            = match.MatchId,
            HomeTeam           = match.HomeTeam,
            AwayTeam           = match.AwayTeam,
            Team               = team,
            Outcome            = request.Outcome,
            Odds               = odds,
            Probability        = Math.Round(prob, 4),
            Edge               = Math.Round(edgeVal, 4),
            Stake              = stake,
            DateTimePlaced     = DateTime.UtcNow,
            Result             = "Pending",
            SportType          = match.SportType,
            LineMovementStatus = movement.ToString(),
        };

        _log.LogBet(bet);

        return Ok(new
        {
            Bet      = bet,
            Warnings = vResult.Warnings,
            Note     = vResult.Warnings.Any(w => w.Contains("20%"))
                ? "Edge > 20% detected — manually verify Poisson inputs before this match starts."
                : null
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/history
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("history")]
    public ActionResult<List<BetHistory>> GetHistory() => Ok(_log.GetHistory());

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/bankroll
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("bankroll")]
    public ActionResult<Bankroll> GetBankroll() => Ok(EnrichedBankroll());

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/result/{id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Record a bet result. Provide ClosingOdds to enable CLV calculation.
    /// CLV = (PlacedOdds / ClosingOdds − 1) × 100%.
    /// Positive CLV = you beat the market — long-term profitable signal.
    /// </summary>
    [HttpPost("result/{id}")]
    public ActionResult UpdateResult(Guid id, [FromBody] UpdateResultRequest request)
    {
        if (request.Result is not ("Win" or "Loss"))
            return BadRequest("Result must be \"Win\" or \"Loss\".");

        var bet = _log.GetById(id);
        if (bet is null)           return NotFound($"Bet {id} not found.");
        if (bet.Result != "Pending") return BadRequest("Result already recorded.");

        decimal pnl = request.Result == "Win" ? bet.Stake * (bet.Odds - 1m) : -bet.Stake;

        double? clvValue = null;
        if (request.ClosingOdds.HasValue && request.ClosingOdds.Value > 0)
            clvValue = Math.Round(_clv.CalculateCLV(bet.Odds, request.ClosingOdds.Value), 2);

        _log.UpdateResult(id, request.Result, pnl, request.ClosingOdds, clvValue);
        _bankroll.UpdateAfterResult(bet.Stake, bet.Odds, request.Result);

        return Ok(new
        {
            BetId       = id,
            Result      = request.Result,
            PnL         = pnl,
            CLV         = clvValue,
            CLVLabel    = clvValue.HasValue ? _clv.Interpret(clvValue.Value) : "N/A"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public ActionResult GetStats()
    {
        var (total, wins, losses, totalPnL, avgCLV) = _log.GetStats();
        var streak = _log.GetCurrentStreak();
        return Ok(new
        {
            Total         = total,
            Wins          = wins,
            Losses        = losses,
            WinRate       = total > 0 ? Math.Round((double)wins / total * 100, 1) : 0,
            TotalPnL      = totalPnL,
            AvgCLV        = avgCLV.HasValue ? Math.Round(avgCLV.Value, 2) : (double?)null,
            CLVLabel      = avgCLV.HasValue ? _clv.Interpret(avgCLV.Value) : "No data yet",
            CurrentStreak = streak,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/rejected
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("rejected")]
    public ActionResult GetRejected() => Ok(_log.GetRejected());

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/settings
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public ActionResult<BettingConfig> GetSettings() => Ok(_cfg.Get());

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /Betting/settings
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Update live betting configuration. Changes apply immediately — no restart needed.
    /// Also invalidates the odds cache so the new timing window takes effect.
    /// </summary>
    [HttpPut("settings")]
    public ActionResult UpdateSettings([FromBody] BettingConfig updated)
    {
        // Safety guards: prevent obviously broken config
        if (updated.EdgeThreshold < 0.01 || updated.EdgeThreshold > 0.50)
            return BadRequest("EdgeThreshold must be between 1% and 50%.");
        if (updated.MaxStakePercent < 0.005 || updated.MaxStakePercent > 0.10)
            return BadRequest("MaxStakePercent must be between 0.5% and 10%.");
        if (updated.PreMatchMinHours < 0.5 || updated.PreMatchMinHours >= updated.PreMatchMaxHours)
            return BadRequest("PreMatchMinHours must be < PreMatchMaxHours and ≥ 0.5.");

        _cfg.Update(updated);
        _odds.InvalidateCache();

        return Ok(new { Message = "Settings updated.", Config = _cfg.Get() });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/refresh
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("refresh")]
    public ActionResult RefreshOdds()
    {
        _odds.InvalidateCache();
        return Ok(new { Message = "Odds cache cleared." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/prediction/{matchId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Poisson prediction detail for a single match: lambdas,
    /// model probabilities, and implied probabilities from the current odds.
    /// Useful for manually verifying the model before placing a bet.
    /// </summary>
    [HttpGet("prediction/{matchId}")]
    public ActionResult GetPrediction(string matchId)
    {
        var match = _odds.GetPreMatchOdds().FirstOrDefault(m => m.MatchId == matchId);
        if (match is null)
            return NotFound($"Match '{matchId}' not found or outside the betting window.");

        var p = _poisson.Predict(match);
        return Ok(new
        {
            MatchId         = match.MatchId,
            HomeTeam        = match.HomeTeam,
            AwayTeam        = match.AwayTeam,
            SportType       = match.SportType,
            HomeLambda      = match.HomeLambda,
            AwayLambda      = match.AwayLambda,
            ModelHomeProb   = Math.Round(p.HomeWinProb, 4),
            ModelDrawProb   = Math.Round(p.DrawProb,    4),
            ModelAwayProb   = Math.Round(p.AwayWinProb, 4),
            ImpliedHomeProb = Math.Round(1.0 / (double)match.HomeOdds, 4),
            ImpliedAwayProb = Math.Round(1.0 / (double)match.AwayOdds, 4),
            ImpliedDrawProb = match.DrawOdds.HasValue
                ? Math.Round(1.0 / (double)match.DrawOdds.Value, 4) : (double?)null,
            HomeEdge = Math.Round(_edge.CalculateEdge(p.HomeWinProb, match.HomeOdds), 4),
            AwayEdge = Math.Round(_edge.CalculateEdge(p.AwayWinProb, match.AwayOdds), 4),
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/bankroll/reset
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets the bankroll state (loss counters, daily limit).
    /// Optionally accepts a new starting amount in the request body.
    /// Does NOT clear bet history — use for starting a new session.
    /// </summary>
    [HttpPost("bankroll/reset")]
    public ActionResult ResetBankroll([FromBody] decimal? newAmount = null)
    {
        _bankroll.Reset(newAmount);
        return Ok(new { Message = "Bankroll reset.", Bankroll = EnrichedBankroll() });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/parlays
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns multi-leg parlay combos built from the current GOOD_BET opportunities.
    /// One combo per leg count (2-leg, 3-leg, 4-leg), using the highest-scored selections.
    /// Same-match legs are excluded to avoid correlation.
    /// </summary>
    [HttpGet("parlays")]
    public ActionResult<List<ParlayCombo>> GetParlays()
    {
        var bankroll = EnrichedBankroll();
        if (bankroll.IsStopLossTriggered)
            return Ok(new List<ParlayCombo>());

        // Reuse the same opportunity pipeline from GetOpportunities
        var config   = _cfg.Get();
        var preMatch = _odds.GetPreMatchOdds();
        var opportunities = new List<BetOpportunity>();
        var now      = DateTime.UtcNow;

        foreach (var match in preMatch)
        {
            var prediction = _poisson.Predict(match);
            var candidates = new List<(string Outcome, string Team, decimal Odds, decimal? PrevOdds, double Prob)>
            {
                ("Home", match.HomeTeam, match.HomeOdds, match.PreviousHomeOdds, prediction.HomeWinProb),
                ("Away", match.AwayTeam, match.AwayOdds, match.PreviousAwayOdds, prediction.AwayWinProb),
            };
            if (match.DrawOdds.HasValue && match.SportType == SportType.EPL)
                candidates.Add(("Draw", "Draw", match.DrawOdds.Value, match.PreviousDrawOdds, prediction.DrawProb));

            foreach (var (outcome, team, odds, prevOdds, prob) in candidates)
            {
                var edgeVal = _edge.CalculateEdge(prob, odds);
                if (edgeVal < config.EdgeThreshold) continue;

                var movement   = _lineMovement.GetMovement(odds, prevOdds);
                var hoursUntil = (match.MatchStartTime - now).TotalHours;
                var stake      = Math.Min(_sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll), bankroll.MaxStakePerBet);

                opportunities.Add(new BetOpportunity
                {
                    MatchId           = match.MatchId,
                    HomeTeam          = match.HomeTeam,
                    AwayTeam          = match.AwayTeam,
                    Team              = team,
                    Outcome           = outcome,
                    Odds              = odds,
                    Probability       = Math.Round(prob, 4),
                    Edge              = Math.Round(edgeVal, 4),
                    SuggestedStake    = stake,
                    SportType         = match.SportType,
                    MatchStartTime    = match.MatchStartTime,
                    HoursUntilKickoff = Math.Round(hoursUntil, 2),
                    PreviousOdds      = prevOdds,
                    LineMovementStatus = movement.ToString(),
                });
            }
        }

        var validated = _aiValidator.Validate(opportunities);
        foreach (var opp in opportunities)
            opp.AiValidation = validated.FirstOrDefault(v => v.MatchId == opp.MatchId && v.Outcome == opp.Outcome);

        return Ok(_parlay.BuildCombos(opportunities));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats/sport
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns win/loss/PnL breakdown per sport for the analytics view.</summary>
    [HttpGet("stats/sport")]
    public ActionResult GetStatsBySport() => Ok(_log.GetStatsBySport());

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/export/csv
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Downloads the full bet history as a CSV file.</summary>
    [HttpGet("export/csv")]
    public IActionResult ExportCsv()
    {
        var history = _log.GetHistory();
        var lines   = new List<string>
        {
            "Id,HomeTeam,AwayTeam,Team,Outcome,SportType,Odds,ClosingOdds,CLV,Edge,Stake,PnL,Result,LineMovement,DatePlaced"
        };

        foreach (var b in history)
        {
            lines.Add(string.Join(",",
                b.Id,
                Escape(b.HomeTeam), Escape(b.AwayTeam), Escape(b.Team),
                b.Outcome, b.SportType,
                b.Odds, b.ClosingOdds?.ToString() ?? "",
                b.CLV?.ToString("F2") ?? "",
                b.Edge.ToString("F4"), b.Stake, b.PnL,
                b.Result, b.LineMovementStatus,
                b.DateTimePlaced.ToString("o")));
        }

        var csv  = string.Join("\n", lines);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", $"bet-history-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static string ComputeConfidence(double prob, double edge) =>
        edge >= 0.15 && prob >= 0.60 ? "High" :
        edge >= 0.10 || prob >= 0.58 ? "Medium" : "Low";

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Merge core bankroll with live exposure and tilt data from logging service.</summary>
    private Bankroll EnrichedBankroll()
    {
        var b = _bankroll.GetBankroll();
        var config = _cfg.Get();
        b.TotalExposure        = _log.GetTotalExposure();
        b.ConsecutiveLosses    = _log.GetConsecutiveLosses();
        b.MaxConsecutiveLosses = config.MaxConsecutiveLosses;
        return b;
    }
}
