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

    // EdgeThreshold=0.04 → bonusLow=0.07, bonusHigh=0.10
    // HighEdgeThreshold=0.15 (test uses 0.18 to trigger it)
    private static readonly BettingConfig DefaultConfig = new()
    {
        EdgeThreshold     = 0.04,
        HighEdgeThreshold = 0.15,
        PreMatchMinHours  = 1.0,
        PreMatchMaxHours  = 336.0,
    };

    public AIValidatorServiceTests()
    {
        _configMock.Setup(c => c.Get()).Returns(DefaultConfig);
        _service = new AIValidatorService(_configMock.Object);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static BetOpportunity MakeOpp(
        double edge              = 0.08,
        decimal odds             = 2.50m,
        string lineMovement      = "Stable",
        double hoursUntilKickoff = 24.0,
        SportType sport          = SportType.AFL,
        string matchId           = "MATCH-001",
        string outcome           = "Home",
        double probability       = 0.45)   // between 0.35 and 0.50 — no prob bonus or penalty
    {
        return new BetOpportunity
        {
            MatchId            = matchId,
            Team               = "TeamA",
            Outcome            = outcome,
            Edge               = edge,
            Odds               = odds,
            Probability        = probability,
            LineMovementStatus = lineMovement,
            HoursUntilKickoff  = hoursUntilKickoff,
            SportType          = sport,
        };
    }

    // ── GOOD_BET scenarios ───────────────────────────────────────────────────

    [Fact]
    public void Validate_SolidEdge_StableLine_ReturnsGoodBet()
    {
        // base 5 + edge>0.07(+1) = 6, prob 0.45 no adj, no flags → GOOD_BET
        var result = _service.Validate([MakeOpp(edge: 0.08)]);

        result.Should().HaveCount(1);
        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(6);
        result[0].Flags.Should().BeEmpty();
    }

    [Fact]
    public void Validate_HighEdgeTen_ScoreBoostTwo_ReturnsGoodBet()
    {
        // base 5 + edge>0.10(+2) = 7, prob 0.45 no adj, edge 0.12 < 0.15 no HighEdge
        var result = _service.Validate([MakeOpp(edge: 0.12)]);

        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(7);
        result[0].Flags.Should().NotContain(ValidationFlags.HighEdge);
    }

    [Fact]
    public void Validate_Steaming_AddsScoreBonusAndFlag()
    {
        // base 5 + edge>0.07(+1) + Steaming(+1) = 7, GOOD_BET
        var result = _service.Validate([MakeOpp(lineMovement: "Steaming", edge: 0.08)]);

        result[0].Flags.Should().Contain(ValidationFlags.Steaming);
        result[0].Decision.Should().Be("GOOD_BET");
        result[0].Score.Should().Be(7);
    }

    // ── SKIP scenarios ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_EdgeBelowMinimum_ReturnsSkip()
    {
        // edge 0.03 < threshold 0.04 → SKIP regardless of other factors
        var result = _service.Validate([MakeOpp(edge: 0.03)]);

        result[0].Decision.Should().Be("SKIP");
    }

    [Fact]
    public void Validate_Drifting_ReturnsSkip()
    {
        // Drifting → LineMovingAgainst flag, −2, forced SKIP
        var result = _service.Validate([MakeOpp(lineMovement: "Drifting", edge: 0.08)]);

        result[0].Decision.Should().Be("SKIP");
        result[0].Flags.Should().Contain(ValidationFlags.LineMovingAgainst);
    }

    [Fact]
    public void Validate_HighEdge_LineMovingAgainst_OddsTooLow_AllFlagged()
    {
        // HighEdge(0.18 > 0.15) + Drifting + OddsTooLow(1.30) → SKIP (forced by Drifting)
        var opp    = MakeOpp(edge: 0.18, odds: 1.30m, lineMovement: "Drifting");
        var result = _service.Validate([opp]);

        result[0].Decision.Should().Be("SKIP");
        result[0].Flags.Should().Contain(ValidationFlags.HighEdge);
        result[0].Flags.Should().Contain(ValidationFlags.LineMovingAgainst);
        result[0].Flags.Should().Contain(ValidationFlags.OddsTooLow);
    }

    // ── RISKY scenarios ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_HighEdgeFlag_ReducesScore_ReturnsRisky()
    {
        // base 5 + edge>0.10(+2) - HighEdge(−2) = 5 → RISKY
        var result = _service.Validate([MakeOpp(edge: 0.18)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Score.Should().Be(5);
        result[0].Flags.Should().Contain(ValidationFlags.HighEdge);
    }

    [Fact]
    public void Validate_OddsTooLow_ReturnsRisky()
    {
        // base 5 + edge>0.07(+1) - OddsTooLow(−1) = 5 → RISKY
        var result = _service.Validate([MakeOpp(odds: 1.30m, edge: 0.08)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Score.Should().Be(5);
        result[0].Flags.Should().Contain(ValidationFlags.OddsTooLow);
    }

    [Fact]
    public void Validate_OddsHighVariance_ReturnsRisky()
    {
        // base 5 + edge>0.07(+1) - HighVariance(−1) = 5 → RISKY
        var result = _service.Validate([MakeOpp(odds: 4.50m, edge: 0.08)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Score.Should().Be(5);
        result[0].Flags.Should().Contain(ValidationFlags.HighVariance);
    }

    [Fact]
    public void Validate_EplLowEdge_ReturnsRisky()
    {
        // base 5, edge 0.06 < 0.07 (no EV bonus), EPL + edge < 0.08 → EplLowEdge(−1) = 4 → RISKY
        var result = _service.Validate([MakeOpp(edge: 0.06, sport: SportType.EPL)]);

        result[0].Decision.Should().Be("RISKY");
        result[0].Flags.Should().Contain(ValidationFlags.EplLowEdge);
    }

    [Fact]
    public void Validate_EplEdgeAtThreshold_NoEplFlag()
    {
        // EPL + edge = 0.08 exactly → NOT EplLowEdge (strict <)
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
        // In parlay mode edge < threshold is NOT an automatic SKIP
        var result = _service.ValidateForParlay([MakeOpp(edge: 0.03)]);

        result[0].Decision.Should().NotBe("SKIP");
    }

    [Fact]
    public void ValidateForParlay_CorrelationSuppressed()
    {
        // Parlay mode disables correlation detection — ParlayService enforces it structurally
        var opps = new List<BetOpportunity>
        {
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Home"),
            MakeOpp(edge: 0.08, matchId: "MATCH-001", outcome: "Away"),
        };

        var results = _service.ValidateForParlay(opps);

        results.Should().NotContain(r => r.Flags.Contains(ValidationFlags.CorrelatedBet));
    }

    // ── Probability scoring ───────────────────────────────────────────────────

    [Fact]
    public void Validate_HighProbFavourite_ScoreBoost()
    {
        // prob > 0.65 → +2; base 5 + edge>0.07(+1) + prob(+2) = 8
        var result = _service.Validate([MakeOpp(edge: 0.08, probability: 0.70)]);

        result[0].Score.Should().Be(8);
        result[0].Decision.Should().Be("GOOD_BET");
    }

    [Fact]
    public void Validate_Longshot_ScorePenalty()
    {
        // prob < 0.35 → -1; base 5 + edge>0.07(+1) - longshot(-1) = 5 → RISKY
        var result = _service.Validate([MakeOpp(edge: 0.08, probability: 0.28)]);

        result[0].Score.Should().Be(5);
        result[0].Decision.Should().Be("RISKY");
    }

    // ── Bad timing ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_KickoffTooClose_BadTimingFlag()
    {
        // kickoff in 0.5h < PreMatchMinHours(1h) → BAD_TIMING flag
        var result = _service.Validate([MakeOpp(edge: 0.08, hoursUntilKickoff: 0.5)]);

        result[0].Flags.Should().Contain(ValidationFlags.BadTiming);
    }

    [Fact]
    public void Validate_KickoffTooFarOut_BadTimingFlag()
    {
        // kickoff in 400h > PreMatchMaxHours(336h) → BAD_TIMING flag
        var result = _service.Validate([MakeOpp(edge: 0.08, hoursUntilKickoff: 400)]);

        result[0].Flags.Should().Contain(ValidationFlags.BadTiming);
    }
}
