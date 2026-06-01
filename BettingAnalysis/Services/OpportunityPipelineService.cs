using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

public class OpportunityPipelineService : IOpportunityPipelineService
{
    private readonly IOddsService          _odds;
    private readonly IPoissonService       _poisson;
    private readonly IEdgeService          _edge;
    private readonly IBetSizingService     _sizing;
    private readonly ILineMovementService  _lineMovement;
    private readonly IValidationService    _validation;
    private readonly IBettingConfigService _cfg;

    public OpportunityPipelineService(
        IOddsService         odds,
        IPoissonService      poisson,
        IEdgeService         edge,
        IBetSizingService    sizing,
        ILineMovementService lineMovement,
        IValidationService   validation,
        IBettingConfigService cfg)
    {
        _odds         = odds;
        _poisson      = poisson;
        _edge         = edge;
        _sizing       = sizing;
        _lineMovement = lineMovement;
        _validation   = validation;
        _cfg          = cfg;
    }

    public async Task<List<BetOpportunity>> BuildOpportunitiesAsync(Bankroll bankroll)
    {
        var config   = _cfg.Get();
        var preMatch = _odds.GetPreMatchOdds();
        var opps     = new List<BetOpportunity>();
        var now      = DateTime.UtcNow;

        foreach (var match in preMatch)
        {
            var prediction = _poisson.Predict(match);

            foreach (var (outcome, team, odds, prevOdds, prob) in Candidates(match, prediction))
            {
                var edgeVal    = _edge.CalculateEdge(prob, odds);
                var movement   = _lineMovement.GetMovement(odds, prevOdds);
                var hoursUntil = (match.MatchStartTime - now).TotalHours;
                var stake      = Math.Min(_sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll), bankroll.MaxStakePerBet);
                var preCheck   = await _validation.ValidateAsync(match, team, odds, edgeVal, stake, movement);

                opps.Add(new BetOpportunity
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

        return opps;
    }

    public Task<List<BetOpportunity>> BuildParlayPoolAsync(Bankroll bankroll)
    {
        var config   = _cfg.Get();
        var preMatch = _odds.GetPreMatchOdds();
        var opps     = new List<BetOpportunity>();
        var now      = DateTime.UtcNow;

        foreach (var match in preMatch)
        {
            var prediction = _poisson.Predict(match);

            foreach (var (outcome, team, odds, prevOdds, prob) in Candidates(match, prediction))
            {
                var edgeVal = _edge.CalculateEdge(prob, odds);
                if (edgeVal < config.ParlayMinEdge) continue;

                var movement   = _lineMovement.GetMovement(odds, prevOdds);
                var hoursUntil = (match.MatchStartTime - now).TotalHours;
                var stake      = Math.Min(_sizing.CalculateStake(prob, odds, bankroll.AvailableBankroll), bankroll.MaxStakePerBet);

                opps.Add(new BetOpportunity
                {
                    MatchId            = match.MatchId,
                    HomeTeam           = match.HomeTeam,
                    AwayTeam           = match.AwayTeam,
                    Team               = team,
                    Outcome            = outcome,
                    Odds               = odds,
                    Probability        = Math.Round(prob, 4),
                    Edge               = Math.Round(edgeVal, 4),
                    SuggestedStake     = stake,
                    SportType          = match.SportType,
                    MatchStartTime     = match.MatchStartTime,
                    HoursUntilKickoff  = Math.Round(hoursUntil, 2),
                    PreviousOdds       = prevOdds,
                    LineMovementStatus = movement.ToString(),
                    ConfidenceLevel    = ComputeConfidence(prob, edgeVal),
                });
            }
        }

        return Task.FromResult(opps);
    }

    private static IEnumerable<(string Outcome, string Team, decimal Odds, decimal? PrevOdds, double Prob)>
        Candidates(MatchOdds match, PredictionResult prediction)
    {
        yield return ("Home", match.HomeTeam, match.HomeOdds, match.PreviousHomeOdds, prediction.HomeWinProb);
        yield return ("Away", match.AwayTeam, match.AwayOdds, match.PreviousAwayOdds, prediction.AwayWinProb);
        if (match.DrawOdds.HasValue && TheOddsApiService.IsSoccerLeague(match.SportType))
            yield return ("Draw", "Draw", match.DrawOdds.Value, match.PreviousDrawOdds, prediction.DrawProb);
    }

    private static string ComputeConfidence(double prob, double edge) =>
        edge >= 0.15 && prob >= 0.60 ? "High" :
        edge >= 0.10 || prob >= 0.58 ? "Medium" : "Low";
}
