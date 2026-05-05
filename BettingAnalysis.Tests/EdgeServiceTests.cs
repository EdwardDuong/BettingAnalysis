using BettingAnalysis.Services;

namespace BettingAnalysis.Tests;

public class EdgeServiceTests
{
    private readonly EdgeService _sut = new();

    [Fact]
    public void Zero_edge_when_model_matches_implied_probability()
    {
        // odds = 2.00 → implied = 0.50; if model also says 50% → edge = 0
        var edge = _sut.CalculateEdge(0.50, 2.00m);
        Assert.InRange(edge, -0.001, 0.001);
    }

    [Fact]
    public void Positive_edge_when_model_probability_exceeds_implied()
    {
        // odds = 3.00 → implied ≈ 0.333; model = 0.45 → positive edge
        var edge = _sut.CalculateEdge(0.45, 3.00m);
        Assert.True(edge > 0, $"Expected positive edge but got {edge:F4}");
    }

    [Fact]
    public void Negative_edge_when_model_probability_below_implied()
    {
        // odds = 1.50 → implied ≈ 0.667; model = 0.55 → negative edge
        var edge = _sut.CalculateEdge(0.55, 1.50m);
        Assert.True(edge < 0, $"Expected negative edge but got {edge:F4}");
    }

    [Fact]
    public void Edge_scales_with_odds()
    {
        // Same model prob, higher odds → larger edge
        var edge1 = _sut.CalculateEdge(0.40, 2.50m);
        var edge2 = _sut.CalculateEdge(0.40, 3.50m);
        Assert.True(edge2 > edge1, "Higher odds should produce larger edge for same model prob");
    }

    [Fact]
    public void Edge_equals_model_minus_implied_probability()
    {
        // odds = 2.00 → implied = 0.50; model = 0.70 → edge = 0.20
        var edge = _sut.CalculateEdge(0.70, 2.00m);
        Assert.InRange(edge, 0.199, 0.201);
    }
}
