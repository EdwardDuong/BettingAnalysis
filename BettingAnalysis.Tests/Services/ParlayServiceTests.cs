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

    // ── BuildDailyDoubleAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DailyDouble_SingleLegClearingTargetAlone_IsUsedWhenNothingBetterExists()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.50m, 0.50, 0.10, 7, "GOOD_BET"),
        };

        var pick = await _service.BuildDailyDoubleAsync(opportunities, _bankroll);

        pick.Should().NotBeNull();
        pick!.Legs.Should().Be(1);
        pick.RiskLabel.Should().Be("Single");
        pick.CombinedOdds.Should().Be(2.50m);
    }

    [Fact]
    public async Task DailyDouble_PrefersSaferParlayOverAWeakerSingleLegThatAlsoClearsTarget()
    {
        // Leg A alone clears the 2.0x target (odds=2.0, prob=0.50) but is inefficient:
        // ln(2.0)/-ln(0.5) = 1.0. Legs B+C are individually short of the target but far
        // more efficient (ln(1.5)/-ln(0.9) = 3.85 each) and together clear it (1.5*1.5
        // = 2.25) with much higher combined probability (0.81 vs A's 0.50). The greedy
        // efficiency ordering must pick B+C, not the naive "take the single that clears
        // it" answer.
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("A", 2.00m, 0.50, 0.10, 7, "GOOD_BET"),
            Opportunity("B", 1.50m, 0.90, 0.10, 7, "GOOD_BET"),
            Opportunity("C", 1.50m, 0.90, 0.10, 7, "GOOD_BET"),
        };

        var pick = await _service.BuildDailyDoubleAsync(opportunities, _bankroll);

        pick.Should().NotBeNull();
        pick!.Legs.Should().Be(2);
        pick.RiskLabel.Should().Be("Parlay");
        pick.Selections.Select(s => s.MatchId).Should().BeEquivalentTo(new[] { "B", "C" });
        pick.CombinedProb.Should().BeApproximately(0.81, 0.0001);
    }

    [Fact]
    public async Task DailyDouble_NoCombinationReachesTarget_ReturnsNull()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 1.20m, 0.85, 0.05, 6, "GOOD_BET"),
        };

        var pick = await _service.BuildDailyDoubleAsync(opportunities, _bankroll);

        pick.Should().BeNull();
    }

    [Fact]
    public async Task DailyDouble_StopsAtConfiguredMaxLegs_EvenIfTargetNotYetReached()
    {
        _configMock.Setup(c => c.Get()).Returns(new BettingConfig { DailyDoubleMaxLegs = 2 });

        // Needs 3 legs of 1.3x to clear 2.0x (1.3^3 = 2.197); capped at 2 legs
        // (1.3^2 = 1.69 < 2.0), so no valid pick should be produced.
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 1.30m, 0.80, 0.05, 6, "GOOD_BET"),
            Opportunity("M2", 1.30m, 0.80, 0.05, 6, "GOOD_BET"),
            Opportunity("M3", 1.30m, 0.80, 0.05, 6, "GOOD_BET"),
        };

        var pick = await _service.BuildDailyDoubleAsync(opportunities, _bankroll);

        pick.Should().BeNull();
    }

    [Fact]
    public async Task DailyDouble_DriftingLineIsExcluded_SharesBaseEligibleFilterWithCombos()
    {
        var opportunities = new List<BetOpportunity>
        {
            Opportunity("M1", 2.50m, 0.50, 0.10, 7, "GOOD_BET", lineMovement: "Drifting"),
        };

        var pick = await _service.BuildDailyDoubleAsync(opportunities, _bankroll);

        pick.Should().BeNull();
    }
}
