using BettingAnalysis.Data;
using BettingAnalysis.Data.Repositories;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static BettingAnalysis.Services.LineMovement;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// Integration tests for ValidationService covering the 11 CRITICAL rules.
/// Uses in-memory database for testing.
/// </summary>
public class ValidationServiceIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IBettingConfigService _configService;
    private readonly IBankrollService _bankrollService;
    private readonly IBettingLoggingService _loggingService;
    private readonly ILineMovementService _lineMovementService;
    private readonly IValidationService _validationService;

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
            })
            .Build();

        // Set up DI with in-memory database
        var services = new ServiceCollection();

        // Add configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Capture the DB name once so ALL scopes share the same in-memory store.
        // If Guid.NewGuid() were inside the lambda, every BettingDbContext would get
        // a different database and reads would see an empty store.
        var testDbName = $"TestDb_{Guid.NewGuid()}";
        services.AddDbContext<BettingDbContext>(options =>
            options.UseInMemoryDatabase(testDbName));

        // Add repositories
        services.AddScoped<IBetRepository, BetRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBankrollSnapshotRepository, BankrollSnapshotRepository>();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Critical));

        // Add services
        services.AddSingleton<IBettingConfigService, BettingConfigService>();
        services.AddSingleton<IBankrollService, BankrollService>();
        services.AddSingleton<IBettingLoggingService, BettingLoggingService>();
        services.AddSingleton<ILineMovementService, LineMovementService>();
        services.AddSingleton<IValidationService, ValidationService>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed default user in database
        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BettingDbContext>();
            context.Database.EnsureCreated();
        }

        // Get service instances
        _configService = _serviceProvider.GetRequiredService<IBettingConfigService>();
        _bankrollService = _serviceProvider.GetRequiredService<IBankrollService>();
        _loggingService = _serviceProvider.GetRequiredService<IBettingLoggingService>();
        _lineMovementService = _serviceProvider.GetRequiredService<ILineMovementService>();
        _validationService = _serviceProvider.GetRequiredService<IValidationService>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    [Fact]
    public void ShouldAcceptBet_WhenAllConditionsValid()
    {
        // Arrange: Perfect scenario - all rules pass
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void ShouldRejectBet_WhenTooCloseToKickoff()
    {
        // Arrange: Rule #1 - Timing window (kickoff in 30 minutes)
        var match = CreateMatch(kickoffInHours: 0.5);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("too close"));
    }

    [Fact]
    public void ShouldRejectBet_WhenEdgeTooLow()
    {
        // Arrange: Rule #2 - Edge threshold (edge = 3%, below 5% minimum)
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.03, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Edge") && v.Contains("3") && v.Contains("5"));
    }

    [Fact]
    public void ShouldWarnBet_WhenEdgeSuspiciouslyHigh()
    {
        // Arrange: Rule #3 - High edge warning (edge = 25%, above 20% threshold)
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 3.00m, 0.25, 100m, Stable);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("Edge") && w.Contains("25") && w.Contains("20"));
    }

    [Fact]
    public void ShouldRejectBet_WhenLineMovingAgainst()
    {
        // Arrange: Rule #4 - Line movement (drifting odds = market disagrees)
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Drifting);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("drifting") || v.Contains("Bet blocked"));
    }

    [Fact]
    public void ShouldRejectBet_WhenStopLossTriggered()
    {
        // Arrange: Rule #5 - Stop loss triggered (20% drawdown)
        SimulateLosses(2500m);  // Lose 2500 from 10000 = 25% loss
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("SYSTEM HALTED") || v.Contains("stop-loss"));
    }

    [Fact]
    public void ShouldRejectBet_WhenDailyLossLimitReached()
    {
        // Arrange: Rule #6 - Daily loss limit (10% = 1000)
        SimulateDailyLosses(1100m);
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Daily loss limit reached"));
    }

    [Fact]
    public void ShouldRejectBet_WhenStakeTooLarge()
    {
        // Arrange: Rule #7 - Stake maximum (3% of 10,000 = 300)
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 500m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Stake") && v.Contains("500") && v.Contains("300"));
    }

    [Fact]
    public void ShouldRejectBet_WhenExposureLimitReached()
    {
        // Arrange: Rule #8 - Exposure limit (10% = 1000)
        PlacePendingBet(800m);
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 300m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Exposure limit") || v.Contains("exposure"));
    }

    [Fact]
    public void ShouldRejectBet_WhenTiltProtectionActive()
    {
        // Arrange: Rule #9 - Tilt protection (3 consecutive losses)
        SimulateConsecutiveLosses(3);
        var match = CreateMatch(kickoffInHours: 24);

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Tilt protection active"));
    }

    [Fact]
    public void ShouldRejectBet_WhenTooManyBetsOnSameMatch()
    {
        // Arrange: Rule #10 - Max bets per match (limit = 2)
        PlacePendingBet(100m, "MATCH-001");
        PlacePendingBet(100m, "MATCH-001");
        var match = CreateMatch(kickoffInHours: 24, matchId: "MATCH-001");

        // Act
        var result = _validationService.Validate(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Already 2 bet") || v.Contains("on this match"));
    }

    // ── Test Helpers ──────────────────────────────────────────────────────────

    private MatchOdds CreateMatch(double kickoffInHours, string matchId = "TEST-MATCH")
    {
        return new MatchOdds
        {
            MatchId = matchId,
            HomeTeam = "Arsenal",
            AwayTeam = "Chelsea",
            HomeOdds = 2.10m,
            DrawOdds = 3.40m,
            AwayOdds = 3.80m,
            MatchStartTime = DateTime.UtcNow.AddHours(kickoffInHours),
            SportType = SportType.EPL
        };
    }

    private void SimulateLosses(decimal totalLoss)
    {
        decimal remaining = totalLoss;
        while (remaining > 0)
        {
            decimal stake = Math.Min(remaining, 100m);
            var bet = new BetHistory
            {
                MatchId = "LOSS-" + Guid.NewGuid(),
                HomeTeam = "Team A",
                AwayTeam = "Team B",
                Team = "Team A",
                Outcome = "Win",
                Odds = 2.0m,
                Probability = 0.5,
                Edge = 0.05,
                Stake = stake,
                Result = "Loss",
                PnL = -stake,
                SportType = SportType.EPL
            };
            _loggingService.LogBet(bet);
            _loggingService.UpdateResult(bet.Id, "Loss", -stake, null, null);
            _bankrollService.UpdateAfterResult(stake, 2.0m, "Loss");
            remaining -= stake;
        }
    }

    private void SimulateDailyLosses(decimal amount)
    {
        SimulateLosses(amount);
    }

    private void SimulateConsecutiveLosses(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var bet = new BetHistory
            {
                MatchId = "LOSS-" + i,
                HomeTeam = "Team A",
                AwayTeam = "Team B",
                Team = "Team A",
                Outcome = "Win",
                Odds = 2.0m,
                Probability = 0.5,
                Edge = 0.05,
                Stake = 50m,
                Result = "Loss",
                PnL = -50m,
                SportType = SportType.EPL,
                DateTimePlaced = DateTime.UtcNow.AddHours(-i)
            };
            _loggingService.LogBet(bet);
            _loggingService.UpdateResult(bet.Id, "Loss", -50m, null, null);
            _bankrollService.UpdateAfterResult(50m, 2.0m, "Loss");
        }
    }

    private void PlacePendingBet(decimal stake, string? matchId = null)
    {
        var bet = new BetHistory
        {
            MatchId = matchId ?? "PENDING-" + Guid.NewGuid(),
            HomeTeam = "Team A",
            AwayTeam = "Team B",
            Team = "Team A",
            Outcome = "Win",
            Odds = 2.0m,
            Probability = 0.5,
            Edge = 0.05,
            Stake = stake,
            Result = "Pending",
            PnL = 0,
            SportType = SportType.EPL
        };
        _loggingService.LogBet(bet);
        _bankrollService.ReserveStake(stake);
    }
}
