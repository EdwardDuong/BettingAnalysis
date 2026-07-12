using BettingAnalysis.Services;
using FluentAssertions;

namespace BettingAnalysis.Tests.Services;

public class LineMovementServiceTests
{
    private readonly LineMovementService _sut = new();

    [Fact]
    public void NoPreviousOdds_ReturnsStable()
    {
        _sut.GetMovement(2.10m, previousOdds: null).Should().Be(LineMovement.Stable);
    }

    [Fact]
    public void ZeroOrNegativePreviousOdds_ReturnsStable()
    {
        // Malformed historical data shouldn't be treated as a real signal.
        _sut.GetMovement(2.10m, previousOdds: 0m).Should().Be(LineMovement.Stable);
    }

    [Fact]
    public void OddsShortened_AboveThreshold_ReturnsSteaming()
    {
        // 2.25 -> 2.10: implied prob 0.444 -> 0.476, delta +3.2% >= 3% threshold
        _sut.GetMovement(2.10m, previousOdds: 2.25m).Should().Be(LineMovement.Steaming);
    }

    [Fact]
    public void OddsLengthened_AboveThreshold_ReturnsDrifting()
    {
        // 1.95 -> 2.10: implied prob 0.513 -> 0.476, delta -3.7% >= 3% threshold
        _sut.GetMovement(2.10m, previousOdds: 1.95m).Should().Be(LineMovement.Drifting);
    }

    [Fact]
    public void SmallMovement_BelowThreshold_ReturnsStable()
    {
        // 2.10 -> 2.12: implied prob shift well under 3%
        _sut.GetMovement(2.12m, previousOdds: 2.10m).Should().Be(LineMovement.Stable);
    }

    [Fact]
    public void Steaming_ShouldNotBlock()
    {
        _sut.ShouldBlock(LineMovement.Steaming).Should().BeFalse();
    }

    [Fact]
    public void Drifting_ShouldBlock()
    {
        _sut.ShouldBlock(LineMovement.Drifting).Should().BeTrue();
    }

    [Fact]
    public void Stable_ShouldNotBlock()
    {
        _sut.ShouldBlock(LineMovement.Stable).Should().BeFalse();
    }
}
