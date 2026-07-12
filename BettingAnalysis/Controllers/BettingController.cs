using System.Security.Claims;
using BettingAnalysis.Hubs;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using static BettingAnalysis.Services.LineMovement;

namespace BettingAnalysis.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class BettingController : ControllerBase
{
    private readonly IOddsService                    _odds;
    private readonly IPoissonService                 _poisson;
    private readonly IEdgeService                    _edge;
    private readonly IBetSizingService               _sizing;
    private readonly IBankrollService                _bankroll;
    private readonly IBettingLoggingService          _log;
    private readonly IValidationService              _validation;
    private readonly ILineMovementService            _lineMovement;
    private readonly ICLVService                     _clv;
    private readonly IBettingConfigService           _cfg;
    private readonly IAIValidatorService             _aiValidator;
    private readonly IParlayService                  _parlay;
    private readonly IOpportunityPipelineService     _pipeline;
    private readonly IHubContext<BettingHub>         _hub;
    private readonly IBankrollSnapshotRepository     _snapshots;

    public BettingController(
        IOddsService                 odds,
        IPoissonService              poisson,
        IEdgeService                 edge,
        IBetSizingService            sizing,
        IBankrollService             bankroll,
        IBettingLoggingService       log,
        IValidationService           validation,
        ILineMovementService         lineMovement,
        ICLVService                  clv,
        IBettingConfigService        cfg,
        IAIValidatorService          aiValidator,
        IParlayService               parlay,
        IOpportunityPipelineService  pipeline,
        IHubContext<BettingHub>      hub,
        IBankrollSnapshotRepository  snapshots)
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
        _pipeline     = pipeline;
        _hub          = hub;
        _snapshots    = snapshots;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/opportunities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all pre-match value bet opportunities, enriched with AI validation.
    ///
    /// Pipeline:
    ///   1.  OddsService       → matches within betting window (Rules #1, #4)
    ///   2.  PoissonService    → outcome probabilities
    ///   3.  EdgeService       → model edge vs implied probability (Rule #2)
    ///   4.  LineMovement      → Steaming / Drifting / Stable
    ///   5.  BetSizingService  → half-Kelly stake (Rule #3)
    ///   6.  AIValidatorService → Score, Decision, Flags for all opportunities
    ///   7.  Sort by AI score descending (GOOD_BET first, then RISKY)
    ///
    /// Returns empty list if stop-loss triggered (Rule #5).
    /// </summary>
    [HttpGet("opportunities")]
    public async Task<ActionResult<List<BetOpportunity>>> GetOpportunities()
    {
        var userId   = CurrentUserId();
        var bankroll = await EnrichedBankrollAsync(userId);
        if (bankroll.IsStopLossTriggered)
            return Ok(new List<BetOpportunity>());

        var config       = _cfg.Get();
        var opportunities = await _pipeline.BuildOpportunitiesAsync(userId, bankroll);

        var validated = _aiValidator.Validate(opportunities);
        foreach (var opp in opportunities)
        {
            opp.AiValidation = validated.FirstOrDefault(v => v.MatchId == opp.MatchId && v.Outcome == opp.Outcome);
            opp.SuggestedStake = opp.AiValidation?.Decision switch
            {
                "GOOD_BET" => Math.Min(opp.SuggestedStake, config.GoodBetMaxStake),
                "RISKY"    => Math.Min(opp.SuggestedStake, config.RiskyMaxStake),
                _          => 0m,
            };
        }

        ScaleStakesToExposureBudget(opportunities, bankroll);

        return Ok(opportunities
            .OrderBy(o => o.AiValidation?.Decision == "GOOD_BET" ? 0 : o.AiValidation?.Decision == "RISKY" ? 1 : 2)
            .ThenByDescending(o => o.AiValidation?.Score ?? 0)
            .ThenByDescending(o => o.Edge)
            .ToList());
    }

    /// <summary>
    /// Each opportunity's SuggestedStake is computed independently against the same
    /// AvailableBankroll (see OpportunityPipelineService), as if it were the only bet
    /// being placed. Taken together, several simultaneously-displayed suggestions can
    /// sum to more than the exposure budget the account actually has room for — the
    /// individual numbers aren't wrong, but showing them un-scaled implies you could
    /// take all of them at face value, which ValidationService's exposure check (Rule
    /// #8) would then reject at placement time anyway. Scale every suggestion down
    /// proportionally so the displayed total fits what's actually placeable right now.
    /// </summary>
    internal static void ScaleStakesToExposureBudget(List<BetOpportunity> opportunities, Bankroll bankroll)
    {
        var remainingBudget = Math.Max(0m, bankroll.MaxExposure - bankroll.TotalExposure);
        var totalSuggested  = opportunities.Sum(o => o.SuggestedStake);
        if (totalSuggested <= remainingBudget || totalSuggested <= 0m) return;

        var scale = remainingBudget / totalSuggested;
        foreach (var opp in opportunities)
            opp.SuggestedStake = Math.Round(opp.SuggestedStake * scale, 2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/place
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("place")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("place-bet")]
    public async Task<ActionResult<object>> PlaceBet([FromBody] PlaceBetRequest request)
    {
        var userId   = CurrentUserId();
        var preMatch = await _odds.GetPreMatchOddsAsync();
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

        var bankroll = await EnrichedBankrollAsync(userId);
        var stake    = Math.Min(
            request.CustomStake ?? _sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll),
            bankroll.MaxStakePerBet);

        var vResult = await _validation.ValidateAsync(userId, match, team, odds, edgeVal, stake, movement);

        if (!vResult.IsValid)
        {
            _log.LogRejected(userId, match.MatchId, team, request.Outcome, vResult.Violations);
            return BadRequest(new { Violations = vResult.Violations, Warnings = vResult.Warnings });
        }

        await _bankroll.ReserveStakeAsync(userId, stake);

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

        await _log.LogBetAsync(userId, bet);

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
    public async Task<ActionResult<List<BetHistory>>> GetHistory(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _log.GetHistoryPagedAsync(CurrentUserId(), page, pageSize);

        Response.Headers["X-Total-Count"]  = total.ToString();
        Response.Headers["X-Page"]         = page.ToString();
        Response.Headers["X-Page-Size"]    = pageSize.ToString();
        Response.Headers["X-Total-Pages"]  = ((int)Math.Ceiling((double)total / pageSize)).ToString();

        return Ok(items);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/bankroll
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("bankroll")]
    public async Task<ActionResult<Bankroll>> GetBankroll()
        => Ok(await EnrichedBankrollAsync(CurrentUserId()));

    // ─────────────────────────────────────────────────────────────────────────
    // POST /Betting/result/{id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Record a bet result. Provide ClosingOdds to enable CLV calculation.
    /// CLV = (PlacedOdds / ClosingOdds − 1) × 100%.
    /// Positive CLV = you beat the market — long-term profitable signal.
    /// </summary>
    [HttpPost("result/{id}")]
    public async Task<ActionResult> UpdateResult(Guid id, [FromBody] UpdateResultRequest request)
    {
        var userId = CurrentUserId();
        var bet = await _log.GetByIdAsync(id, userId);
        if (bet is null)             return NotFound($"Bet {id} not found.");
        if (bet.Result != "Pending") return BadRequest("Result already recorded.");

        decimal pnl = request.Result == "Win" ? bet.Stake * (bet.Odds - 1m) : -bet.Stake;

        double? clvValue = null;
        if (request.ClosingOdds.HasValue && request.ClosingOdds.Value > 0)
            clvValue = Math.Round(_clv.CalculateCLV(bet.Odds, request.ClosingOdds.Value), 2);

        await _log.UpdateResultAsync(id, userId, request.Result, pnl, request.ClosingOdds, clvValue);
        await _bankroll.UpdateAfterResultAsync(userId, bet.Stake, bet.Odds, request.Result);

        await _hub.Clients.All.SendAsync("BankrollUpdated", await EnrichedBankrollAsync(userId));

        return Ok(new
        {
            BetId    = id,
            Result   = request.Result,
            PnL      = pnl,
            CLV      = clvValue,
            CLVLabel = clvValue.HasValue ? _clv.Interpret(clvValue.Value) : "N/A"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        var userId      = CurrentUserId();
        var statsTask   = _log.GetStatsAsync(userId);
        var streakTask  = _log.GetCurrentStreakAsync(userId);
        var stakedTask  = _log.GetTotalStakedAsync(userId);
        var edgeTask    = _log.GetAverageEdgeAsync(userId);
        await Task.WhenAll(statsTask, streakTask, stakedTask, edgeTask);

        var (total, wins, losses, totalPnL, avgCLV) = statsTask.Result;
        var streak      = streakTask.Result;
        var totalStaked = stakedTask.Result;
        var avgEdge     = edgeTask.Result;
        var roi         = totalStaked > 0
            ? Math.Round((double)(totalPnL / totalStaked) * 100, 1) : 0.0;

        return Ok(new
        {
            Total         = total,
            Wins          = wins,
            Losses        = losses,
            WinRate       = total > 0 ? Math.Round((double)wins / total * 100, 1) : 0,
            TotalPnL      = totalPnL,
            TotalStaked   = totalStaked,
            ROI           = roi,
            AvgEdge       = avgEdge,
            AvgCLV        = avgCLV.HasValue ? Math.Round(avgCLV.Value, 2) : (double?)null,
            CLVLabel      = avgCLV.HasValue ? _clv.Interpret(avgCLV.Value) : "No data yet",
            CurrentStreak = Math.Abs(streak),
            StreakType    = streak > 0 ? "Win" : streak < 0 ? "Loss" : "None",
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/rejected
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("rejected")]
    public ActionResult GetRejected() => Ok(_log.GetRejected(CurrentUserId()));

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
    [Authorize(Roles = "Admin")]
    [HttpPut("settings")]
    public ActionResult UpdateSettings([FromBody] BettingConfig updated)
    {
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
    public async Task<ActionResult> GetPrediction(string matchId)
    {
        var match = (await _odds.GetPreMatchOddsAsync()).FirstOrDefault(m => m.MatchId == matchId);
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
    [Authorize(Roles = "Admin")]
    [HttpPost("bankroll/reset")]
    public async Task<ActionResult> ResetBankroll([FromBody] decimal? newAmount = null)
    {
        var userId = CurrentUserId();
        await _bankroll.ResetAsync(userId, newAmount);
        return Ok(new { Message = "Bankroll reset.", Bankroll = await EnrichedBankrollAsync(userId) });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/bankroll/history?days=90
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns one bankroll data point per day for the last N days.</summary>
    [HttpGet("bankroll/history")]
    public async Task<ActionResult> GetBankrollHistory([FromQuery] int days = 90)
    {
        days = Math.Clamp(days, 7, 365);
        var from      = DateTime.UtcNow.AddDays(-days);
        var snapshots = await _snapshots.GetByDateRangeAsync(CurrentUserId(), from, DateTime.UtcNow);

        var daily = snapshots
            .GroupBy(s => s.SnapshotDate.Date)
            .Select(g => g.OrderByDescending(s => s.SnapshotDate).First())
            .OrderBy(s => s.SnapshotDate)
            .Select(s => new
            {
                Date         = s.SnapshotDate.ToString("yyyy-MM-dd"),
                Bankroll     = s.TotalBankroll,
                TotalPnL     = s.TotalPnL,
                ROI          = Math.Round(s.ROI * 100, 2),
                WinRate      = Math.Round(s.WinRate * 100, 1),
                TotalBets    = s.TotalBetsPlaced,
                ConsecLosses = s.ConsecutiveLosses,
            })
            .ToList();

        return Ok(daily);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/parlays
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns multi-leg parlay combos built from the current GOOD_BET opportunities.
    /// One combo per leg count (3-leg, 4-leg, 5-leg), using the highest-scored selections.
    /// Same-match legs are excluded to avoid correlation.
    /// </summary>
    [HttpGet("parlays")]
    public async Task<ActionResult<List<ParlayCombo>>> GetParlays()
    {
        var bankroll = await EnrichedBankrollAsync(CurrentUserId());
        if (bankroll.IsStopLossTriggered)
            return Ok(new List<ParlayCombo>());

        var config       = _cfg.Get();
        var opportunities = await _pipeline.BuildParlayPoolAsync(bankroll);

        var validated = _aiValidator.ValidateForParlay(opportunities);
        foreach (var opp in opportunities)
        {
            opp.AiValidation = validated.FirstOrDefault(v => v.MatchId == opp.MatchId && v.Outcome == opp.Outcome);
            opp.SuggestedStake = opp.AiValidation?.Decision switch
            {
                "GOOD_BET" => Math.Min(opp.SuggestedStake, config.GoodBetMaxStake),
                "RISKY"    => Math.Min(opp.SuggestedStake, config.RiskyMaxStake),
                _          => 0m,
            };
        }

        return Ok(await _parlay.BuildCombosAsync(opportunities, bankroll));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/daily-double
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the single safest way to clear BettingConfig.DailyDoubleTargetOdds
    /// (default 2x) combined odds using today's opportunities — a single leg if one
    /// alone clears it more safely than any combination, otherwise a small parlay.
    /// Computed live on every call (not persisted/locked once per day) — recomputing
    /// with fresher odds can change the answer.
    /// </summary>
    [HttpGet("daily-double")]
    public async Task<ActionResult<ParlayCombo>> GetDailyDouble()
    {
        var bankroll = await EnrichedBankrollAsync(CurrentUserId());
        if (bankroll.IsStopLossTriggered)
            return Ok(new { Message = "System halted — stop-loss triggered." });

        var opportunities = await _pipeline.BuildParlayPoolAsync(bankroll);
        var validated      = _aiValidator.ValidateForParlay(opportunities);
        foreach (var opp in opportunities)
            opp.AiValidation = validated.FirstOrDefault(v => v.MatchId == opp.MatchId && v.Outcome == opp.Outcome);

        var pick = await _parlay.BuildDailyDoubleAsync(opportunities, bankroll);
        return pick is null
            ? Ok(new { Message = "No safe way to clear the target odds with today's opportunities." })
            : Ok(pick);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats/sport
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns win/loss/PnL breakdown per sport for the analytics view.</summary>
    [HttpGet("stats/sport")]
    public async Task<ActionResult> GetStatsBySport()
        => Ok(await _log.GetStatsBySportAsync(CurrentUserId()));

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/stats/calibration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns predicted-probability-vs-actual-win-rate buckets for settled bets.
    /// Use this to sanity-check whether the Poisson/calibration model's probabilities
    /// are actually predictive, rather than trusting edge/Kelly output on faith.
    /// </summary>
    [HttpGet("stats/calibration")]
    public async Task<ActionResult> GetCalibrationReport()
        => Ok(await _log.GetCalibrationReportAsync(CurrentUserId()));

    // ─────────────────────────────────────────────────────────────────────────
    // GET /Betting/export/csv
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Downloads the full bet history as a CSV file.</summary>
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var history = await _log.GetHistoryAsync(CurrentUserId());
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

        var csv   = string.Join("\n", lines);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", $"bet-history-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Escape(string s)
    {
        // Strip leading formula-injection characters, then always quote to handle newlines/commas.
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            s = "'" + s;
        s = s.Replace("\r", "").Replace("\n", " ");
        return $"\"{s.Replace("\"", "\"\"")}\"";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 1;

    private async Task<Bankroll> EnrichedBankrollAsync(int userId)
    {
        var b      = await _bankroll.GetBankrollAsync(userId);
        var config = _cfg.Get();
        b.TotalExposure        = await _log.GetTotalExposureAsync(userId);
        b.ConsecutiveLosses    = await _log.GetConsecutiveLossesAsync(userId);
        b.MaxConsecutiveLosses = config.MaxConsecutiveLosses;
        return b;
    }
}
