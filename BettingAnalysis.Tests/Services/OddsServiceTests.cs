using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// Covers OddsService.ApplyPreviousOdds — the fix for real (non-mock) odds never
/// getting Previous* fields populated, which silently made LineMovementService
/// report every match as "Stable" when running against the real Odds API.
/// </summary>
public class OddsServiceTests
{
    private static MatchOdds Match(string id, decimal home, decimal? draw, decimal away) => new()
    {
        MatchId  = id,
        HomeTeam = "Home", AwayTeam = "Away",
        HomeOdds = home, DrawOdds = draw, AwayOdds = away,
        MatchStartTime = DateTime.UtcNow.AddHours(3),
        SportType = SportType.EPL,
    };

    [Fact]
    public void FirstFetch_WithNoPriorCache_LeavesPreviousOddsNull()
    {
        var fresh = new List<MatchOdds> { Match("M1", 2.10m, 3.40m, 3.80m) };

        OddsService.ApplyPreviousOdds(fresh, previous: null);

        fresh[0].PreviousHomeOdds.Should().BeNull();
        fresh[0].PreviousDrawOdds.Should().BeNull();
        fresh[0].PreviousAwayOdds.Should().BeNull();
    }

    [Fact]
    public void SubsequentFetch_CarriesForwardMatchingMatchIdAsPrevious()
    {
        var previous = new List<MatchOdds> { Match("M1", 2.25m, 3.40m, 3.60m) };
        var fresh    = new List<MatchOdds> { Match("M1", 2.10m, 3.40m, 3.80m) };

        OddsService.ApplyPreviousOdds(fresh, previous);

        fresh[0].PreviousHomeOdds.Should().Be(2.25m);
        fresh[0].PreviousDrawOdds.Should().Be(3.40m);
        fresh[0].PreviousAwayOdds.Should().Be(3.60m);
    }

    [Fact]
    public void NewMatchNotInPreviousCache_LeavesPreviousOddsNull()
    {
        var previous = new List<MatchOdds> { Match("M1", 2.25m, 3.40m, 3.60m) };
        var fresh    = new List<MatchOdds> { Match("M2", 1.90m, null, 1.90m) };

        OddsService.ApplyPreviousOdds(fresh, previous);

        fresh[0].PreviousHomeOdds.Should().BeNull();
    }

    [Fact]
    public void CarriedForwardPreviousOdds_ProduceCorrectLineMovementClassification()
    {
        // End-to-end check that the carried-forward odds actually feed LineMovementService
        // correctly: home odds shortened 2.25 -> 2.10, so this should classify as Steaming.
        var previous = new List<MatchOdds> { Match("M1", 2.25m, 3.40m, 3.60m) };
        var fresh    = new List<MatchOdds> { Match("M1", 2.10m, 3.40m, 3.60m) };
        OddsService.ApplyPreviousOdds(fresh, previous);

        var movement = new LineMovementService().GetMovement(fresh[0].HomeOdds, fresh[0].PreviousHomeOdds);

        movement.Should().Be(LineMovement.Steaming);
    }
}
