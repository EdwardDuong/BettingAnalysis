using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Moq;

namespace BettingAnalysis.Tests.Services;

public class BetSizingServiceTests
{
    private readonly Mock<IBettingConfigService> _configMock = new();
    private readonly BetSizingService _service;

    public BetSizingServiceTests()
    {
        _configMock.Setup(c => c.Get()).Returns(new BettingConfig
        {
            KellyFraction   = 0.5,
            MaxStakePercent = 0.03
        });
        _service = new BetSizingService(_configMock.Object);
    }

    [Fact]
    public void CalculateStake_WithPositiveEdge_ReturnsPositiveStake()
    {
        // prob=0.52, odds=2.00: fullKelly=(0.52*1-0.48)/1=0.04, half=0.02 → 10000*0.02=200
        var stake = _service.CalculateStake(0.52, 2.00m, 10_000m);

        stake.Should().Be(200m);
    }

    [Fact]
    public void CalculateStake_WithNegativeEdge_ReturnsZero()
    {
        // prob=0.40, odds=2.00: fullKelly=-0.20 → clamped to 0
        var stake = _service.CalculateStake(0.40, 2.00m, 10_000m);

        stake.Should().Be(0m);
    }

    [Fact]
    public void CalculateStake_AtExactBreakeven_ReturnsZero()
    {
        // prob=0.50, odds=2.00: fullKelly=0 exactly
        var stake = _service.CalculateStake(0.50, 2.00m, 10_000m);

        stake.Should().Be(0m);
    }

    [Fact]
    public void CalculateStake_CapsAt_MaxStakePercent()
    {
        // prob=0.70, odds=2.00: fullKelly=0.40, half=0.20 → capped at 0.03 → 300
        var stake = _service.CalculateStake(0.70, 2.00m, 10_000m);

        stake.Should().Be(300m);
    }

    [Fact]
    public void CalculateStake_IsProportionalToBankroll()
    {
        var stake5k  = _service.CalculateStake(0.52, 2.00m, 5_000m);
        var stake10k = _service.CalculateStake(0.52, 2.00m, 10_000m);

        stake10k.Should().Be(stake5k * 2);
    }

    [Fact]
    public void CalculateStake_WithFullKelly_DoublesUnderCap()
    {
        _configMock.Setup(c => c.Get()).Returns(new BettingConfig
        {
            KellyFraction   = 1.0,   // Full Kelly
            MaxStakePercent = 0.10   // Higher cap so we're not capped
        });

        // prob=0.52, odds=2.00: fullKelly=0.04, full=0.04 → 10000*0.04=400
        var stake = _service.CalculateStake(0.52, 2.00m, 10_000m);

        stake.Should().Be(400m);
    }

    [Fact]
    public void CalculateStake_WithHighOdds_AppliesKellyCorrectly()
    {
        // prob=0.35, odds=4.00: b=3, fullKelly=(0.35*3-0.65)/3=(1.05-0.65)/3=0.40/3≈0.133, half≈0.067 → capped at 0.03 → 300
        var stake = _service.CalculateStake(0.35, 4.00m, 10_000m);

        stake.Should().Be(300m);
    }

    [Fact]
    public void CalculateStake_WithOddsAtOne_ReturnsZeroInsteadOfThrowing()
    {
        // b = odds - 1 = 0, so full Kelly (probability*b - q)/b divides by zero and
        // produces NaN, which throws OverflowException when cast from double to decimal.
        var stake = _service.CalculateStake(0.60, 1.00m, 10_000m);

        stake.Should().Be(0m);
    }

    [Fact]
    public void CalculateStake_WithOddsBelowOne_ReturnsZeroInsteadOfThrowing()
    {
        // Malformed odds data (< 1.0) should never reach here in practice, but the
        // service must not crash on it — b is negative, Kelly is undefined.
        var stake = _service.CalculateStake(0.60, 0.50m, 10_000m);

        stake.Should().Be(0m);
    }
}
