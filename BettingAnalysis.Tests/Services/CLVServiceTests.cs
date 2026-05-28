using BettingAnalysis.Services;
using FluentAssertions;

namespace BettingAnalysis.Tests.Services;

public class CLVServiceTests
{
    private readonly CLVService _service = new();

    // ── CalculateCLV ─────────────────────────────────────────────────────────

    [Fact]
    public void CalculateCLV_BeatClosingLine_ReturnsPositive()
    {
        // Placed at 2.20, closed at 2.00 → CLV = (2.20/2.00 - 1)*100 = +10%
        var clv = _service.CalculateCLV(2.20m, 2.00m);

        clv.Should().BeApproximately(10.0, 0.01);
    }

    [Fact]
    public void CalculateCLV_WorseThanClosingLine_ReturnsNegative()
    {
        // Placed at 1.90, closed at 2.10 → CLV = (1.90/2.10 - 1)*100 ≈ -9.52%
        var clv = _service.CalculateCLV(1.90m, 2.10m);

        clv.Should().BeApproximately(-9.524, 0.01);
    }

    [Fact]
    public void CalculateCLV_SameAsClosingLine_ReturnsZero()
    {
        var clv = _service.CalculateCLV(2.00m, 2.00m);

        clv.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void CalculateCLV_ZeroClosingOdds_ReturnsZero()
    {
        // Guard against division by zero
        var clv = _service.CalculateCLV(2.00m, 0m);

        clv.Should().Be(0.0);
    }

    [Fact]
    public void CalculateCLV_NegativeClosingOdds_ReturnsZero()
    {
        var clv = _service.CalculateCLV(2.00m, -1m);

        clv.Should().Be(0.0);
    }

    // ── Interpret ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5.0,  "Excellent")]
    [InlineData(10.0, "Excellent")]
    [InlineData(2.0,  "Good")]
    [InlineData(4.9,  "Good")]
    [InlineData(0.0,  "Marginal")]
    [InlineData(1.9,  "Marginal")]
    [InlineData(-2.0, "Warning")]
    [InlineData(-0.1, "Warning")]
    [InlineData(-2.1, "Poor — review model")]
    [InlineData(-10.0,"Poor — review model")]
    public void Interpret_ReturnsCorrectLabel(double clv, string expected)
    {
        _service.Interpret(clv).Should().Be(expected);
    }

    // ── GetColour ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2.0,  "green")]
    [InlineData(5.0,  "green")]
    [InlineData(0.0,  "yellow")]
    [InlineData(1.9,  "yellow")]
    [InlineData(-0.1, "red")]
    [InlineData(-5.0, "red")]
    public void GetColour_ReturnsCorrectColour(double clv, string expected)
    {
        _service.GetColour(clv).Should().Be(expected);
    }
}
