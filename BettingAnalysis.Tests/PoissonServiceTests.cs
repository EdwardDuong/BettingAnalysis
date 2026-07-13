using BettingAnalysis.Models;
using BettingAnalysis.Services;

namespace BettingAnalysis.Tests;

public class PoissonServiceTests
{
    private readonly PoissonService _sut = new();

    private static MatchOdds EplMatch(double homeLambda, double awayLambda) => new()
    {
        MatchId       = "TEST-001",
        HomeTeam      = "Home",
        AwayTeam      = "Away",
        HomeOdds      = 2.00m,
        AwayOdds      = 3.50m,
        DrawOdds      = 3.20m,
        MatchStartTime = DateTime.UtcNow.AddHours(3),
        SportType     = SportType.EPL,
        HomeLambda    = homeLambda,
        AwayLambda    = awayLambda,
    };

    private static MatchOdds BinaryMatch(double homeLambda, double awayLambda) => new()
    {
        MatchId       = "TEST-002",
        HomeTeam      = "Home",
        AwayTeam      = "Away",
        HomeOdds      = 1.80m,
        AwayOdds      = 2.10m,
        MatchStartTime = DateTime.UtcNow.AddHours(3),
        SportType     = SportType.NBA,
        HomeLambda    = homeLambda,
        AwayLambda    = awayLambda,
    };

    private static MatchOdds SoccerMatch(SportType sport, double homeLambda, double awayLambda) => new()
    {
        MatchId       = "TEST-003",
        HomeTeam      = "Home",
        AwayTeam      = "Away",
        HomeOdds      = 2.00m,
        AwayOdds      = 3.50m,
        DrawOdds      = 3.20m,
        MatchStartTime = DateTime.UtcNow.AddHours(3),
        SportType     = sport,
        HomeLambda    = homeLambda,
        AwayLambda    = awayLambda,
    };

    [Fact]
    public void EPL_probabilities_sum_to_one()
    {
        var result = _sut.Predict(EplMatch(1.5, 1.1));
        var total  = result.HomeWinProb + result.DrawProb + result.AwayWinProb;
        Assert.InRange(total, 0.999, 1.001);
    }

    [Fact]
    public void EPL_higher_lambda_home_favours_home_win()
    {
        var result = _sut.Predict(EplMatch(2.5, 0.8));
        Assert.True(result.HomeWinProb > result.AwayWinProb,
            $"Expected home > away but got {result.HomeWinProb:F4} vs {result.AwayWinProb:F4}");
    }

    [Fact]
    public void EPL_equal_lambdas_gives_symmetric_win_probs()
    {
        var result = _sut.Predict(EplMatch(1.4, 1.4));
        Assert.InRange(Math.Abs(result.HomeWinProb - result.AwayWinProb), 0, 0.01);
    }

    [Fact]
    public void Binary_probabilities_sum_to_one()
    {
        var result = _sut.Predict(BinaryMatch(0.6, 0.4));
        var total  = result.HomeWinProb + result.AwayWinProb;
        Assert.InRange(total, 0.999, 1.001);
    }

    [Fact]
    public void Binary_draw_probability_is_zero()
    {
        var result = _sut.Predict(BinaryMatch(0.55, 0.45));
        Assert.Equal(0, result.DrawProb);
    }

    [Fact]
    public void Binary_home_lambda_dominates_correctly()
    {
        var result = _sut.Predict(BinaryMatch(0.75, 0.25));
        Assert.True(result.HomeWinProb > 0.70,
            $"Expected home prob > 0.70 but got {result.HomeWinProb:F4}");
    }

    [Fact]
    public void All_probabilities_are_in_valid_range()
    {
        foreach (var match in new[] { EplMatch(1.2, 0.9), EplMatch(2.2, 1.5), BinaryMatch(0.6, 0.4) })
        {
            var r = _sut.Predict(match);
            Assert.InRange(r.HomeWinProb, 0, 1);
            Assert.InRange(r.DrawProb,    0, 1);
            Assert.InRange(r.AwayWinProb, 0, 1);
        }
    }

    [Fact]
    public void Binary_zero_lambdas_returns_even_split_instead_of_dividing_by_zero()
    {
        // homeLambda + awayLambda = 0 previously produced NaN (0/0), which would
        // propagate into edge/stake calculations downstream.
        var result = _sut.Predict(BinaryMatch(0, 0));

        Assert.Equal(0.5, result.HomeWinProb);
        Assert.Equal(0.5, result.AwayWinProb);
        Assert.Equal(0,   result.DrawProb);
    }

    [Theory]
    [InlineData(SportType.LaLiga)]
    [InlineData(SportType.Bundesliga)]
    [InlineData(SportType.SerieA)]
    [InlineData(SportType.Ligue1)]
    [InlineData(SportType.Eredivisie)]
    [InlineData(SportType.PrimeiraLiga)]
    [InlineData(SportType.MLS)]
    [InlineData(SportType.ChampionsLeague)]
    public void NonEplSoccerLeagues_StillUseDrawInclusivePoissonGrid(SportType sport)
    {
        // Predict() used to special-case only SportType.EPL for the draw-inclusive
        // grid; every other soccer league silently fell through to the no-draw binary
        // model and got DrawProb = 0 even though it has a real Draw market/odds.
        var result = _sut.Predict(SoccerMatch(sport, 1.5, 1.1));

        Assert.True(result.DrawProb > 0, $"{sport} should get a non-zero draw probability from the Poisson grid");
        var total = result.HomeWinProb + result.DrawProb + result.AwayWinProb;
        Assert.InRange(total, 0.999, 1.001);
    }
}
