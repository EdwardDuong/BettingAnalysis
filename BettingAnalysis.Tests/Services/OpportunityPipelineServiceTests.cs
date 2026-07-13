using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Moq;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// OpportunityPipelineService composes Odds -> Poisson -> Edge -> Sizing -> Validation
/// and previously had zero dedicated unit tests, only indirect coverage through
/// controller-level manual testing. EdgeService and LineMovementService are pure/
/// stateless so real instances are used instead of mocks; everything else (odds
/// source, model, sizing, validation, config) is mocked for deterministic inputs.
/// </summary>
public class OpportunityPipelineServiceTests
{
    private const int TestUserId = 42;

    private readonly Mock<IOddsService> _oddsMock = new();
    private readonly Mock<IPoissonService> _poissonMock = new();
    private readonly Mock<IBetSizingService> _sizingMock = new();
    private readonly Mock<IValidationService> _validationMock = new();
    private readonly Mock<IBettingConfigService> _cfgMock = new();
    private readonly OpportunityPipelineService _service;

    private readonly Bankroll _bankroll = new() { AvailableBankroll = 1000m, MaxStakePerBet = 50m };

    private static readonly MatchOdds SoccerMatch = new()
    {
        MatchId = "M1", HomeTeam = "Home1", AwayTeam = "Away1",
        HomeOdds = 2.00m, AwayOdds = 3.50m, DrawOdds = 3.20m,
        MatchStartTime = DateTime.UtcNow.AddHours(3), SportType = SportType.EPL,
    };

    private static readonly MatchOdds NonSoccerMatch = new()
    {
        MatchId = "M2", HomeTeam = "H2", AwayTeam = "A2",
        HomeOdds = 1.80m, AwayOdds = 2.10m, DrawOdds = null,
        MatchStartTime = DateTime.UtcNow.AddHours(5), SportType = SportType.NBA,
    };

    public OpportunityPipelineServiceTests()
    {
        _oddsMock.Setup(o => o.GetPreMatchOddsAsync())
            .ReturnsAsync(new List<MatchOdds> { SoccerMatch, NonSoccerMatch });

        // Home1: prob 0.60 @ 2.00 -> edge 0.20 (High confidence, above HighEdgeThreshold)
        // Away1: prob 0.20 @ 3.50 -> edge -0.30
        // Draw1: prob 0.20 @ 3.20 -> edge -0.36
        _poissonMock.Setup(p => p.Predict(SoccerMatch))
            .Returns(new PredictionResult { HomeWinProb = 0.60, DrawProb = 0.20, AwayWinProb = 0.20 });
        // H2: prob 0.55 @ 1.80 -> edge -0.01; A2: prob 0.45 @ 2.10 -> edge -0.055
        _poissonMock.Setup(p => p.Predict(NonSoccerMatch))
            .Returns(new PredictionResult { HomeWinProb = 0.55, DrawProb = 0, AwayWinProb = 0.45 });

        // Sizing always suggests more than the bankroll's per-bet cap, to prove the
        // pipeline enforces Math.Min(sizing output, bankroll.MaxStakePerBet).
        _sizingMock.Setup(s => s.CalculateStake(It.IsAny<double>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(200m);

        _validationMock.Setup(v => v.ValidateAsync(
                It.IsAny<int>(), It.IsAny<MatchOdds>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<double>(), It.IsAny<decimal>(), It.IsAny<LineMovement>()))
            .ReturnsAsync(new ValidationResult { Warnings = new List<string> { "check odds" } });

        _cfgMock.Setup(c => c.Get()).Returns(new BettingConfig { HighEdgeThreshold = 0.15, ParlayMinEdge = 0.05 });

        _service = new OpportunityPipelineService(
            _oddsMock.Object, _poissonMock.Object, new EdgeService(), _sizingMock.Object,
            new LineMovementService(), _validationMock.Object, _cfgMock.Object);
    }

    [Fact]
    public async Task BuildOpportunitiesAsync_YieldsHomeAwayDrawForSoccer_HomeAwayOnlyForNonSoccer()
    {
        var result = await _service.BuildOpportunitiesAsync(TestUserId, _bankroll);

        result.Should().HaveCount(5, "soccer match yields Home+Away+Draw, non-soccer yields Home+Away only");
        result.Count(o => o.MatchId == "M1").Should().Be(3);
        result.Count(o => o.MatchId == "M2").Should().Be(2);
        result.Should().NotContain(o => o.MatchId == "M2" && o.Outcome == "Draw");
    }

    [Fact]
    public async Task BuildOpportunitiesAsync_ComputesEdgeAndConfidence()
    {
        var result = await _service.BuildOpportunitiesAsync(TestUserId, _bankroll);

        var home1 = result.Single(o => o.MatchId == "M1" && o.Outcome == "Home");
        home1.Edge.Should().BeApproximately(0.20, 0.0001);
        home1.Probability.Should().BeApproximately(0.60, 0.0001);
        home1.ConfidenceLevel.Should().Be("High");

        var away1 = result.Single(o => o.MatchId == "M1" && o.Outcome == "Away");
        away1.Edge.Should().BeApproximately(-0.30, 0.0001);
        away1.ConfidenceLevel.Should().Be("Low");
    }

    [Fact]
    public async Task BuildOpportunitiesAsync_CapsStakeAtBankrollMaxStakePerBet()
    {
        // Sizing mock always returns 200, but bankroll.MaxStakePerBet is 50.
        var result = await _service.BuildOpportunitiesAsync(TestUserId, _bankroll);

        result.Should().OnlyContain(o => o.SuggestedStake == 50m);
    }

    [Fact]
    public async Task BuildOpportunitiesAsync_FlagsHighRiskAndManualCheck_WhenEdgeAtOrAboveHighEdgeThreshold()
    {
        var result = await _service.BuildOpportunitiesAsync(TestUserId, _bankroll);

        var home1 = result.Single(o => o.MatchId == "M1" && o.Outcome == "Home"); // edge 0.20 >= 0.15
        home1.IsHighRisk.Should().BeTrue();
        home1.RequiresManualCheck.Should().BeTrue();

        var away1 = result.Single(o => o.MatchId == "M1" && o.Outcome == "Away"); // edge -0.30
        away1.IsHighRisk.Should().BeFalse();
        away1.RequiresManualCheck.Should().BeFalse();
    }

    [Fact]
    public async Task BuildOpportunitiesAsync_PropagatesValidationWarningsAndUserId()
    {
        var result = await _service.BuildOpportunitiesAsync(TestUserId, _bankroll);

        result.Should().OnlyContain(o => o.ValidationWarnings.Contains("check odds"));

        _validationMock.Verify(v => v.ValidateAsync(
            TestUserId, It.IsAny<MatchOdds>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<double>(), It.IsAny<decimal>(), It.IsAny<LineMovement>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task BuildParlayPoolAsync_ExcludesCandidatesBelowParlayMinEdge()
    {
        // Only Home1 (edge 0.20) clears ParlayMinEdge (0.05); all four other
        // candidates (-0.30, -0.36, -0.01, -0.055) are filtered out.
        var result = await _service.BuildParlayPoolAsync(_bankroll);

        result.Should().ContainSingle();
        result[0].MatchId.Should().Be("M1");
        result[0].Outcome.Should().Be("Home");
    }

    [Fact]
    public async Task BuildParlayPoolAsync_SkipsPreValidation()
    {
        var result = await _service.BuildParlayPoolAsync(_bankroll);

        result.Should().OnlyContain(o => o.ValidationWarnings.Count == 0 && !o.IsHighRisk && !o.RequiresManualCheck,
            "the parlay pool pipeline intentionally skips ValidationService — see IOpportunityPipelineService's doc comment");
        _validationMock.Verify(v => v.ValidateAsync(
            It.IsAny<int>(), It.IsAny<MatchOdds>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<double>(), It.IsAny<decimal>(), It.IsAny<LineMovement>()),
            Times.Never);
    }
}
