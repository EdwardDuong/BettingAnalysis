using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// Integration tests for ValidationService covering the 11 CRITICAL rules.
/// Uses REAL service instances - this is an integration test approach.
/// </summary>
public class ValidationServiceIntegrationTests : IDisposable
{
    private readonly BettingConfigService _configService;
    private readonly BankrollService _bankrollService;
    private readonly BettingLoggingService _loggingService;
    private readonly LineMovementService _lineMovementService;
    private readonly ValidationService _validationService;

    public ValidationServiceIntegrationTests()
    {
        // Create in-memory configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BettingSettings:InitialBankroll"] = "10000",
                ["BettingSettings:EdgeThreshold"] = "0.05",
                ["BettingSettings:HighEdgeThreshold"] = "0.20",
                ["BettingSettings:PreMatchMinHoursAhead"] = "1.0",
                ["BettingSettings:PreMatchMaxHoursAhead"] = "336.0",
                ["BettingSettings:MaxStakePercent"] = "0.03",
                ["BettingSettings:DailyLossLimitPercent"] = "0.10",
                ["BettingSettings:StopLossPercent"] = "0.20",
                ["BettingSettings:MaxExposurePercent"] = "0.10",
                ["BettingSettings:MaxConsecutiveLosses"] = "3",
                ["BettingSettings:MaxBetsPerMatch"] = "2",
                ["BettingSettings:HistoryFilePath"] = $"test-history-{Guid.NewGuid()}.json"
            })
            .Build();

        // Create REAL service instances
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Critical));

        _configService = new BettingConfigService(configuration);
        _bankrollService = new BankrollService(configuration);
        _loggingService = new BettingLoggingService(configuration, loggerFactory.CreateLogger<BettingLoggingService>());
        _lineMovementService = new LineMovementService();

        _validationService = new ValidationService(
            _configService,
            _bankrollService,
            _loggingService,
            _lineMovementService
        );
    }

    public void Dispose()
    {
        // Cleanup test history file
        var config = _configService.Get();
        // Note: Would need HistoryFilePath exposed to clean up properly
    }

    [Fact]
    public void ShouldAcceptBet_WhenAllConditionsValid()
    {
        // Arrange: Perfect scenario - all rules pass
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(
            match,
            team: "Liverpool",
            odds: 2.5m,
            edge: 0.10, // 10% edge - above 5% threshold
            stake: 200m, // Within 3% max stake
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void ShouldRejectBet_WhenKickoffTooClose()
    {
        // Arrange: Match in 30 minutes - below 1 hour minimum
        var match = CreateMatch(kickoffInHours: 0.5);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            200m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("too close"));
    }

    [Fact]
    public void ShouldRejectBet_WhenEdgeBelowThreshold()
    {
        // Arrange: Only 3% edge when minimum is 5%
        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            edge: 0.03, // Below 5% threshold
            200m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("threshold"));
    }

    [Fact]
    public void ShouldRejectBet_WhenLineDrifting()
    {
        // Arrange: Odds drifting - market disagrees
        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            200m,
            LineMovement.Drifting // This will trigger rejection
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("drifting"));
    }

    [Fact]
    public void ShouldRejectBet_WhenStopLossTriggered()
    {
        // Arrange: Simulate 20% cumulative loss
        _bankrollService.UpdateAfterResult(2000m, 2.0m, "Loss"); // Lose $2000

        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            100m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("stop-loss") || v.Contains("SYSTEM HALTED"));
    }

    [Fact]
    public void ShouldRejectBet_WhenDailyLossLimitReached()
    {
        // Arrange: Simulate daily loss of 10% ($1000)
        _bankrollService.UpdateAfterResult(1000m, 2.0m, "Loss");

        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            100m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Daily loss"));
    }

    [Fact]
    public void ShouldRejectBet_WhenStakeExceedsMaximum()
    {
        // Arrange: Try to stake $500 when max is 3% = $300
        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            stake: 500m, // Exceeds $300 max
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("exceeds max-per-bet"));
    }

    [Fact]
    public void ShouldRejectBet_WhenExposureLimitExceeded()
    {
        // Arrange: Create pending bets totaling $900 exposure
        _loggingService.LogBet(new BetHistory
        {
            MatchId = "MATCH1",
            Stake = 500m,
            Result = "Pending",
            DateTimePlaced = DateTime.UtcNow
        });
        _loggingService.LogBet(new BetHistory
        {
            MatchId = "MATCH2",
            Stake = 400m,
            Result = "Pending",
            DateTimePlaced = DateTime.UtcNow
        });

        var match = CreateMatch(24);

        // Act: Try to add $300 more (total would be $1200, limit is $1000 = 10%)
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            stake: 300m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Exposure limit"));
    }

    [Fact]
    public void ShouldRejectBet_WhenTiltProtectionTriggered()
    {
        // Arrange: Log 3 consecutive losses
        for (int i = 0; i < 3; i++)
        {
            var betId = Guid.NewGuid();
            _loggingService.LogBet(new BetHistory
            {
                Id = betId,
                MatchId = $"MATCH{i}",
                Stake = 100m,
                Result = "Pending",
                DateTimePlaced = DateTime.UtcNow.AddHours(-i)
            });
            _loggingService.UpdateResult(betId, "Loss", -100m, null, null);
        }

        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            0.10,
            100m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Tilt protection") || v.Contains("consecutive losses"));
    }

    [Fact]
    public void ShouldRejectBet_WhenTeamBlacklisted()
    {
        // Arrange: Blacklist Manchester United
        var config = _configService.Get();
        config.TeamBlacklist = new List<string> { "Manchester United", "Arsenal" };
        _configService.Update(config);

        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            team: "Manchester United", // Blacklisted!
            2.5m,
            0.10,
            200m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("blacklist"));
    }

    [Fact]
    public void ShouldWarnButNotReject_WhenHighEdge()
    {
        // Arrange: 25% edge - very high, triggers warning
        var match = CreateMatch(24);

        // Act
        var result = _validationService.Validate(
            match,
            "Liverpool",
            2.5m,
            edge: 0.25, // 25% - above 20% threshold
            200m,
            LineMovement.Stable
        );

        // Assert
        result.IsValid.Should().BeTrue("high edge should warn but not block");
        result.Warnings.Should().Contain(w => w.Contains("manually verify") || w.Contains("exceeds"));
    }

    #region Helper Methods

    private MatchOdds CreateMatch(double kickoffInHours)
    {
        return new MatchOdds
        {
            MatchId = Guid.NewGuid().ToString(),
            SportType = SportType.EPL,
            HomeTeam = "Liverpool",
            AwayTeam = "Manchester City",
            MatchStartTime = DateTime.UtcNow.AddHours(kickoffInHours),
            HomeOdds = 2.0m,
            AwayOdds = 3.5m
        };
    }

    #endregion
}
