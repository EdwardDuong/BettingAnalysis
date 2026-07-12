using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Moq;

namespace BettingAnalysis.Tests.Services;

public class ParlayServiceTests
{
    private readonly Mock<IBettingConfigService> _configMock = new();
    private readonly ParlayService _service;
    private readonly Bankroll _bankroll = new() { AvailableBankroll = 10_000m };

    public ParlayServiceTests()
    {
        _configMock.Setup(c => c.Get()).Returns(new BettingConfig());
        _service = new ParlayService(_configMock.Object);
    }

    private static BetOpportunity Opportunity(
        string matchId, decimal odds, double probability, double edge,
        int score, string decision, string lineMovement = "Stable") => new()
    {
        MatchId  = matchId,
        HomeTeam = "Home", AwayTeam = "Away", Team = "Home", Outcome = "Home",
        Odds = odds, Probability = probability, Edge = edge,
        LineMovementStatus = lineMovement,
        AiValidation = new ValidatedBet { MatchId = matchId, Score = score, Decision = decision },
    };

    [Fact]
    public async Task FewerThanThreeEligibleOpportunities_ReturnsNoCombos()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        combos.Should().BeEmpty();
    }

    [Fact]
    public async Task ThreeFavourites_ProducesSafeComboWithMultipliedOddsAndProbability()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M3", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        var safe = combos.Should().ContainSingle(c => c.RiskLabel == "Safe").Subject;
        safe.Legs.Should().Be(3);
        // CombinedOdds/CombinedProb are a straight product across legs — i.e. the
        // service assumes the legs are statistically independent. Documented here
        // so the assumption is visible, not just a review-comment risk.
        safe.CombinedOdds.Should().Be(8.00m);       // 2.00^3
        safe.CombinedProb.Should().BeApproximately(0.216, 0.0001); // 0.60^3
        safe.SuggestedStake.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LegsConcentratedOnFewerDistinctMatches_AreExcludedEvenIfCandidateCountMeetsMinimum()
    {
        // 4 candidates but only 2 distinct matches — the same-match correlation guard
        // (dedup by MatchId in BuildBestCombo) should leave too few usable legs for
        // any 3-leg combo, even though the raw candidate count (4) clears MinLegs.
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "RISKY"),
            Opportunity("M1", 1.80m, 0.65, 0.08, 6, "RISKY"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "RISKY"),
            Opportunity("M2", 1.80m, 0.65, 0.08, 6, "RISKY"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        combos.Should().BeEmpty();
    }

    [Fact]
    public async Task DriftingLine_IsExcludedFromEligiblePool()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "GOOD_BET", lineMovement: "Drifting"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M3", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        combos.Should().BeEmpty("only 2 legs remain eligible once the drifting leg is excluded");
    }

    [Fact]
    public async Task SkipDecision_IsExcludedFromEligiblePool()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "SKIP"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M3", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        combos.Should().BeEmpty();
    }

    [Fact]
    public async Task SuggestedStake_IsCappedAtTierMaxStake()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M2", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
            Opportunity("M3", 2.00m, 0.60, 0.10, 7, "GOOD_BET"),
        };

        var combos = await _service.BuildCombosAsync(opportunities, _bankroll);

        var safe = combos.Should().ContainSingle(c => c.RiskLabel == "Safe").Subject;
        safe.SuggestedStake.Should().BeLessThanOrEqualTo(new BettingConfig().Parlay3MaxStake);
    }
}
