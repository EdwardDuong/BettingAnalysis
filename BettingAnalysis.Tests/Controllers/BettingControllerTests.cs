using BettingAnalysis.Controllers;
using BettingAnalysis.Models;
using FluentAssertions;

namespace BettingAnalysis.Tests.Controllers;

/// <summary>
/// Covers BettingController.ScaleStakesToExposureBudget — the fix for
/// OpportunityPipelineService computing each opportunity's Kelly stake
/// independently against the same AvailableBankroll, so several simultaneously
/// displayed suggestions could jointly exceed the account's exposure budget
/// even though each one individually respected it.
/// </summary>
public class BettingControllerTests
{
    private static Bankroll Bankroll(decimal maxExposure, decimal totalExposure) => new()
    {
        MaxExposure   = maxExposure,
        TotalExposure = totalExposure,
    };

    private static BetOpportunity Opportunity(decimal suggestedStake) => new()
    {
        MatchId = Guid.NewGuid().ToString(),
        SuggestedStake = suggestedStake,
    };

    [Fact]
    public void TotalUnderBudget_LeavesStakesUnchanged()
    {
        var opportunities = new List<BetOpportunity> { Opportunity(100m), Opportunity(150m) };
        var bankroll = Bankroll(maxExposure: 1000m, totalExposure: 0m);

        BettingController.ScaleStakesToExposureBudget(opportunities, bankroll);

        opportunities[0].SuggestedStake.Should().Be(100m);
        opportunities[1].SuggestedStake.Should().Be(150m);
    }

    [Fact]
    public void TotalOverBudget_ScalesAllStakesDownProportionally()
    {
        // Budget is 200; three opportunities suggest 300 total (2x, 1x, ... ratio 2:1)
        var opportunities = new List<BetOpportunity> { Opportunity(200m), Opportunity(100m) };
        var bankroll = Bankroll(maxExposure: 200m, totalExposure: 0m);

        BettingController.ScaleStakesToExposureBudget(opportunities, bankroll);

        // scale = 200/300 = 0.6667
        opportunities[0].SuggestedStake.Should().Be(133.33m);
        opportunities[1].SuggestedStake.Should().Be(66.67m);
        (opportunities[0].SuggestedStake + opportunities[1].SuggestedStake)
            .Should().BeLessThanOrEqualTo(bankroll.MaxExposure);
    }

    [Fact]
    public void ExistingExposure_ReducesAvailableBudget()
    {
        // MaxExposure 1000, already 900 in pending bets -> only 100 of budget left
        var opportunities = new List<BetOpportunity> { Opportunity(150m) };
        var bankroll = Bankroll(maxExposure: 1000m, totalExposure: 900m);

        BettingController.ScaleStakesToExposureBudget(opportunities, bankroll);

        opportunities[0].SuggestedStake.Should().Be(100m);
    }

    [Fact]
    public void ExposureAlreadyAtOrOverLimit_ZerosOutAllSuggestions()
    {
        var opportunities = new List<BetOpportunity> { Opportunity(100m), Opportunity(50m) };
        var bankroll = Bankroll(maxExposure: 1000m, totalExposure: 1000m);

        BettingController.ScaleStakesToExposureBudget(opportunities, bankroll);

        opportunities[0].SuggestedStake.Should().Be(0m);
        opportunities[1].SuggestedStake.Should().Be(0m);
    }

    [Fact]
    public void NoSuggestedStakes_DoesNothing()
    {
        var opportunities = new List<BetOpportunity> { Opportunity(0m), Opportunity(0m) };
        var bankroll = Bankroll(maxExposure: 1000m, totalExposure: 0m);

        BettingController.ScaleStakesToExposureBudget(opportunities, bankroll);

        opportunities[0].SuggestedStake.Should().Be(0m);
        opportunities[1].SuggestedStake.Should().Be(0m);
    }
}
