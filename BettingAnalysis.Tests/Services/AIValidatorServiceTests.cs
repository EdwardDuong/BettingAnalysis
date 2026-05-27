using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Moq;

namespace BettingAnalysis.Tests.Services;

public class AIValidatorServiceTests
{
    private readonly Mock<IBettingConfigService> _configMock = new();
    private readonly AIValidatorService _service;

    private static readonly BettingConfig DefaultConfig = new()
    {
        PreMatchMinHours = 1.0,
        PreMatchMaxHours = 336.0,
    };

    public AIValidatorServiceTests()
    {
        _configMock.Setup(c => c.Get()).Returns(DefaultConfig);
        _service = new AIValidatorService(_configMock.Object);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static BetOpportunity MakeOpp(
        double edge                  = 0.08,
        decimal odds                 = 2.50m,
        string lineMovement          = "Stable",
        double hoursUntilKickoff     = 24.0,
        SportType sport              = SportType.AFL,
        string matchId               = "MATCH-001",
        string outcome               = "Home")
    {
        return new BetOpportunity
        {
            MatchId             = matchId,
            Team                = "TeamA",
            Outcome             = outcome,
            Edge                = edge,
            Odds                = odds,
            LineMovementStatus  = lineMovement,
            HoursUntilKickoff   = hoursUntilKickoff,
            SportType           = sport,
        };
    }

    // ── GOOD_BET scenarios ───────────────────────────────────────────────────

    [Fact]
    public void Validate_SolidEdge_StableLine_ReturnsGoodBet()
    {
        // score: 5 + 1(edge>7%) = 6, no flags
        var result = _service.Validate([MakeOpp(edge: 0.08)]);

        result.Should().HaveCount(1);
        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(6);
        result[0].Flags.Should().BeEmpty();
    }

    [Fact]
    public void Validate_HighEdgeTen_ScoreBoostTwo_ReturnsGoodBet()
    {
        // score: 5 + 2(edge>10%) = 7, no flags (edge=0.12 < 0.15 HIGH_EDGE threshold)
        var result = _service.Validate([MakeOpp(edge: 0.12)]);

        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(7);
    }

    [Fact]
    public void Validate_Steaming_AddsPositiveFlag_NoScorePenalty()
    {
        // Steaming = positive signal tag only, no penalty
        var result = _service.Validate([MakeOpp(lineMovement: "Steaming", edge: 0.08)]);

        result[0].Flags.Should().Contain(ValidationFlags.Steaming);
        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(6);
    }

    // ── SKIP scenarios ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_EdgeBelowMinimum_ReturnsSkip()
    {
        // edge < 5% → immediate SKIP regardless of other factors
        var result = _service.Validate([MakeOpp(edge: 0.03)]);

        result[0].Decision.Should().Be("SKIP");
    }

    [Fact]
    public void Validate_ThreeOrMoreRiskFlags_ReturnsSkip()
    {
        // HIGH_EDGE (edge>15%) + LINE_MOVING_AGAINST + ODDS_TOO_LOW = 3 risk flags
        var opp = MakeOpp(edge: 0.18, odds: 1.30m, lineMovement: "Drifting");
        var result = _service.Validate([opp]);

        result[0].Decision.Should().Be("SKIP");
        result[0].Flags.Should().Contain(ValidationFlags.HighEdge);
        result[0].Flags.Should().Contain(ValidationFlags.LineMovingAgainst);
        result[0].Flags.Should().Contain(ValidationFlags.OddsTooLow);
    }

    // ── RISKY scenarios ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_LineMovingAgainst_ReturnsRisky()
    {
        // score: 5 - 2(Drifting) + 1(edge>7%) = 4, 1 major flag → RISKY
        var result = _service.Validate([MakeOpp(lineMovement: "Drifting", edge: 0.08)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.LineMovingAgainst);
    }

    [Fact]
    public void Validate_HighEdgeFlag_ReturnsRisky()
    {
        // edge=0.18 > 0.15 → HIGH_EDGE flag, score: 5 - 2 + 2 = 5 → RISKY
        var result = _service.Validate([MakeOpp(edge: 0.18)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.HighEdge);
    }

    [Fact]
    public void Validate_OddsTooLow_ReturnsRisky()
    {
        // odds < 1.5 → ODDS_TOO_LOW, score: 5 - 1 + 1 = 5 → RISKY (score ≤ 5)
        var result = _service.Validate([MakeOpp(odds: 1.30m, edge: 0.08)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.OddsTooLow);
    }

    [Fact]
    public void Validate_OddsHighVariance_ReturnsRisky()
    {
        // odds > 3.0 → HIGH_VARIANCE, score: 5 - 1 + 1 = 5 → RISKY
        var result = _service.Validate([MakeOpp(odds: 4.50m, edge: 0.08)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.HighVariance);
    }

    [Fact]
    public void Validate_EplLowEdge_ReturnsRisky()
    {
        // EPL + edge < 8% → EPL_LOW_EDGE flag; score: 5 - 1 + 1 = 5 → RISKY
        var result = _service.Validate([MakeOpp(edge: 0.06, sport: SportType.EPL)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.EplLowEdge);
    }

    [Fact]
    public void Validate_EplEdgeAtThreshold_NoEplFlag()
    {
        // EPL + edge = 8% exactly → NOT EPL_LOW_EDGE (condition is strict <)
        var result = _service.Validate([MakeOpp(edge: 0.08, sport: SportType.EPL)]);

        result[0].Flags.Should().NotContain(ValidationFlags.EplLowEdge);
    }

    // ── Correlation detection ────────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleLegsOnSameMatch_FlagsCorrelation()
    {
        var opps = new List<BetOpportunity>
        {
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Home"),
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Away"),
        };

        var results = _service.Validate(opps);

        // Both should carry the correlated bet flag (count == 2 for the same match)
        results.All(r => r.Flags.Contains(ValidationFlags.CorrelatedBet)).Should().BeTrue();
    }

    [Fact]
    public void Validate_DifferentMatches_NoCorrelationFlag()
    {
        var opps = new List<BetOpportunity>
        {
            MakeOpp(edge: 0.08, matchId: "MATCH-001"),
            MakeOpp(edge: 0.08, matchId: "MATCH-002"),
        };

        var results = _service.Validate(opps);

        results.Should().NotContain(r => r.Flags.Contains(ValidationFlags.CorrelatedBet));
    }

    // ── Parlay mode ──────────────────────────────────────────────────────────

    [Fact]
    public void ValidateForParlay_LowEdge_DoesNotSkip()
    {
        // In parlay mode, edge < 5% is NOT an automatic SKIP
        var result = _service.ValidateForParlay([MakeOpp(edge: 0.03)]);

        result[0].Decision.Should().NotBe("SKIP");
    }

    [Fact]
    public void ValidateForParlay_CorrelationSuppressed()
    {
        // Parlay mode disables correlation detection (ParlayService enforces it structurally)
        var opps = new List<BetOpportunity>
        {
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Home"),
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Away"),
        };

        var results = _service.ValidateForParlay(opps);

        results.Should().NotContain(r => r.Flags.Contains(ValidationFlags.CorrelatedBet));
    }

    // ── Bad timing ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_KickoffTooClose_BadTimingFlag()
    {
        // kickoff in 0.5h < PreMatchMinHours(1h) → BAD_TIMING
        var result = _service.Validate([MakeOpp(edge: 0.08, hoursUntilKickoff: 0.5)]);

        result[0].Flags.Should().Contain(ValidationFlags.BadTiming);
    }

    [Fact]
    public void Validate_KickoffTooFarOut_BadTimingFlag()
    {
        // kickoff in 400h > PreMatchMaxHours(336h) → BAD_TIMING
        var result = _service.Validate([MakeOpp(edge: 0.08, hoursUntilKickoff: 400)]);

        result[0].Flags.Should().Contain(ValidationFlags.BadTiming);
    }
}
