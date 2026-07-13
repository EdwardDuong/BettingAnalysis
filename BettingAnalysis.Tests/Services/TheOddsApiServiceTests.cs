using BettingAnalysis.Services;
using FluentAssertions;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// Covers TheOddsApiService.DampenRatio — the fix for soccer lambda calibration
/// amplifying deviation from the league average instead of damping it, which was
/// most visible on lopsided matches (a big favourite/underdog far from the league's
/// assumed average win rate). See BettingConfig.SoccerCalibrationShrinkage's doc
/// comment for the full explanation.
/// </summary>
public class TheOddsApiServiceTests
{
    [Fact]
    public void Shrinkage_One_ReturnsRatioUnchanged()
    {
        // shrinkage=1.0 must reproduce the old, undamped behaviour exactly.
        TheOddsApiService.DampenRatio(0.6, shrinkage: 1.0).Should().BeApproximately(0.6, 0.0001);
        TheOddsApiService.DampenRatio(2.0, shrinkage: 1.0).Should().BeApproximately(2.0, 0.0001);
    }

    [Fact]
    public void Shrinkage_Zero_AlwaysReturnsOne()
    {
        // shrinkage=0.0 means "ignore this match's own signal entirely" — always the
        // league-average lambda, regardless of how lopsided the match's own odds are.
        TheOddsApiService.DampenRatio(0.1, shrinkage: 0.0).Should().BeApproximately(1.0, 0.0001);
        TheOddsApiService.DampenRatio(5.0, shrinkage: 0.0).Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void Ratio_AtLeagueAverage_IsUnaffectedByShrinkage()
    {
        // A match exactly matching the league average (ratio=1.0) shouldn't move
        // regardless of the shrinkage setting — there's no deviation to dampen.
        TheOddsApiService.DampenRatio(1.0, shrinkage: 0.5).Should().BeApproximately(1.0, 0.0001);
        TheOddsApiService.DampenRatio(1.0, shrinkage: 0.0).Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void Shrinkage_Half_MovesRatioHalfwayTowardOne()
    {
        TheOddsApiService.DampenRatio(0.6, shrinkage: 0.5).Should().BeApproximately(0.8, 0.0001);
        TheOddsApiService.DampenRatio(2.0, shrinkage: 0.5).Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void RealWorldExample_LopsidedMlsMatch_ProducesNarrowerLambdaGapThanUndamped()
    {
        // Real case that motivated this fix: an MLS match where the market-implied
        // Home probability (~28.7%) was well below the assumed league average (50%),
        // and Away (~45.0%) well above the assumed away average (24%). The raw ratio
        // scaling amplified this into a HomeWinProb/AwayWinProb split (13.2%/68%)
        // more extreme than the market itself. With shrinkage, the resulting lambda
        // gap should be meaningfully narrower.
        const double avgHome = 1.45, avgAway = 1.15;
        const double avgHomeWinRate = 0.50, avgAwayWinRate = 0.24;
        const double fairHome = 0.287, fairAway = 0.450;

        double rawHomeRatio = fairHome / avgHomeWinRate;
        double rawAwayRatio = fairAway / avgAwayWinRate;

        double undampedHomeLambda = avgHome * TheOddsApiService.DampenRatio(rawHomeRatio, shrinkage: 1.0);
        double undampedAwayLambda = avgAway * TheOddsApiService.DampenRatio(rawAwayRatio, shrinkage: 1.0);
        double dampedHomeLambda   = avgHome * TheOddsApiService.DampenRatio(rawHomeRatio, shrinkage: 0.5);
        double dampedAwayLambda   = avgAway * TheOddsApiService.DampenRatio(rawAwayRatio, shrinkage: 0.5);

        var undampedGap = undampedAwayLambda - undampedHomeLambda;
        var dampedGap   = dampedAwayLambda - dampedHomeLambda;

        dampedGap.Should().BeLessThan(undampedGap,
            "shrinkage should pull the lopsided lambda split closer together, not leave it as extreme as the raw ratio");
        dampedHomeLambda.Should().BeGreaterThan(undampedHomeLambda);
        dampedAwayLambda.Should().BeLessThan(undampedAwayLambda);
    }
}
