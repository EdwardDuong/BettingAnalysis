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
    private readonly ServiceProvider        _serviceProvider;
    private readonly IBettingConfigService  _configService;
    private readonly IBankrollService       _bankrollService;
    private readonly IBettingLoggingService _loggingService;
    private readonly IValidationService     _validationService;

    public ValidationServiceIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BettingSettings:InitialBankroll"]        = "10000",
                ["BettingSettings:EdgeThreshold"]          = "0.05",
                ["BettingSettings:HighEdgeThreshold"]      = "0.20",
                ["BettingSettings:PreMatchMinHoursAhead"]  = "1.0",
                ["BettingSettings:PreMatchMaxHoursAhead"]  = "336.0",
                ["BettingSettings:MaxStakePercent"]        = "0.03",
                ["BettingSettings:DailyLossLimitPercent"]  = "0.10",
                ["BettingSettings:StopLossPercent"]        = "0.20",
                ["BettingSettings:MaxExposurePercent"]     = "0.10",
                ["BettingSettings:MaxConsecutiveLosses"]   = "3",
                ["BettingSettings:MaxBetsPerMatch"]        = "2",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var testDbName = $"TestDb_{Guid.NewGuid()}";
        services.AddDbContext<BettingDbContext>(options =>
            options.UseInMemoryDatabase(testDbName));

        services.AddScoped<IBetRepository, BetRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBankrollSnapshotRepository, BankrollSnapshotRepository>();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Critical));

        services.AddSingleton<IBettingConfigService, BettingConfigService>();
        services.AddSingleton<IBankrollService, BankrollService>();
        services.AddSingleton<IBettingLoggingService, BettingLoggingService>();
        services.AddSingleton<ILineMovementService, LineMovementService>();
        services.AddSingleton<IValidationService, ValidationService>();

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<BettingDbContext>().Database.EnsureCreated();
        }

        _configService     = _serviceProvider.GetRequiredService<IBettingConfigService>();
        _bankrollService   = _serviceProvider.GetRequiredService<IBankrollService>();
        _loggingService    = _serviceProvider.GetRequiredService<IBettingLoggingService>();
        _validationService = _serviceProvider.GetRequiredService<IValidationService>();
    }

    public void Dispose() => _serviceProvider?.Dispose();

    [Fact]
    public async Task ShouldAcceptBet_WhenAllConditionsValid()
    {
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRejectBet_WhenTooCloseToKickoff()
    {
        var match  = CreateMatch(kickoffInHours: 0.5);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("too close"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenEdgeTooLow()
    {
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.03, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Edge") && v.Contains("3") && v.Contains("5"));
    }

    [Fact]
    public async Task ShouldWarnBet_WhenEdgeSuspiciouslyHigh()
    {
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 3.00m, 0.25, 100m, Stable);

        result.Warnings.Should().Contain(w => w.Contains("Edge") && w.Contains("25") && w.Contains("20"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenLineMovingAgainst()
    {
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Drifting);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("drifting") || v.Contains("Bet blocked"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenStopLossTriggered()
    {
        await SimulateLossesAsync(2500m);
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("SYSTEM HALTED") || v.Contains("stop-loss"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenDailyLossLimitReached()
    {
        await SimulateLossesAsync(1100m);
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Daily loss limit reached"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenStakeTooLarge()
    {
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 500m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Stake") && v.Contains("500") && v.Contains("300"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenExposureLimitReached()
    {
        await PlacePendingBetAsync(800m);
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 300m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Exposure limit") || v.Contains("exposure"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenTiltProtectionActive()
    {
        await SimulateConsecutiveLossesAsync(3);
        var match  = CreateMatch(kickoffInHours: 24);
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Tilt protection active"));
    }

    [Fact]
    public async Task ShouldRejectBet_WhenTooManyBetsOnSameMatch()
    {
        await PlacePendingBetAsync(100m, "MATCH-001");
        await PlacePendingBetAsync(100m, "MATCH-001");
        var match  = CreateMatch(kickoffInHours: 24, matchId: "MATCH-001");
        var result = await _validationService.ValidateAsync(match, "Arsenal", 2.10m, 0.08, 100m, Stable);

        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Already 2 bet") || v.Contains("on this match"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MatchOdds CreateMatch(double kickoffInHours, string matchId = "TEST-MATCH") =>
        new()
        {
            MatchId        = matchId,
            HomeTeam       = "Arsenal",
            AwayTeam       = "Chelsea",
            HomeOdds       = 2.10m,
            DrawOdds       = 3.40m,
            AwayOdds       = 3.80m,
            MatchStartTime = DateTime.UtcNow.AddHours(kickoffInHours),
            SportType      = SportType.EPL
        };

    private async Task SimulateLossesAsync(decimal totalLoss)
    {
        decimal remaining = totalLoss;
        while (remaining > 0)
        {
            decimal stake = Math.Min(remaining, 100m);
            var bet = new BetHistory
            {
                MatchId   = "LOSS-" + Guid.NewGuid(),
                HomeTeam  = "Team A", AwayTeam = "Team B", Team = "Team A",
                Outcome   = "Win", Odds = 2.0m, Probability = 0.5,
                Edge      = 0.05, Stake = stake, Result = "Loss",
                PnL       = -stake, SportType = SportType.EPL
            };
            await _loggingService.LogBetAsync(bet);
            await _loggingService.UpdateResultAsync(bet.Id, "Loss", -stake, null, null);
            await _bankrollService.UpdateAfterResultAsync(stake, 2.0m, "Loss");
            remaining -= stake;
        }
    }

    private async Task SimulateConsecutiveLossesAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var bet = new BetHistory
            {
                MatchId   = "LOSS-" + i,
                HomeTeam  = "Team A", AwayTeam = "Team B", Team = "Team A",
                Outcome   = "Win", Odds = 2.0m, Probability = 0.5,
                Edge      = 0.05, Stake = 50m, Result = "Loss",
                PnL       = -50m, SportType = SportType.EPL,
                DateTimePlaced = DateTime.UtcNow.AddHours(-i)
            };
            await _loggingService.LogBetAsync(bet);
            await _loggingService.UpdateResultAsync(bet.Id, "Loss", -50m, null, null);
            await _bankrollService.UpdateAfterResultAsync(50m, 2.0m, "Loss");
        }
    }

    private async Task PlacePendingBetAsync(decimal stake, string? matchId = null)
    {
        var bet = new BetHistory
        {
            MatchId   = matchId ?? "PENDING-" + Guid.NewGuid(),
            HomeTeam  = "Team A", AwayTeam = "Team B", Team = "Team A",
            Outcome   = "Win", Odds = 2.0m, Probability = 0.5,
            Edge      = 0.05, Stake = stake, Result = "Pending",
            PnL       = 0, SportType = SportType.EPL
        };
        await _loggingService.LogBetAsync(bet);
        await _bankrollService.ReserveStakeAsync(stake);
    }
}
