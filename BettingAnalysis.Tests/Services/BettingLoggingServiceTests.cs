using BettingAnalysis.Data;
using BettingAnalysis.Data.Repositories;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// BettingLoggingService previously had no dedicated unit tests — it was only
/// exercised indirectly through ValidationServiceIntegrationTests. This covers the
/// per-user isolation guarantees directly, plus the calibration report added to make
/// model-probability accuracy checkable against real settled-bet outcomes instead of
/// trusted on faith.
/// </summary>
public class BettingLoggingServiceTests : IDisposable
{
    private const int UserA = 1;
    private const int UserB = 2;

    private readonly ServiceProvider _serviceProvider;
    private readonly IBettingLoggingService _service;

    public BettingLoggingServiceTests()
    {
        var services = new ServiceCollection();
        var dbName = $"LoggingTestDb_{Guid.NewGuid()}";
        services.AddDbContext<BettingDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IBetRepository, BetRepository>();
        services.AddLogging();
        services.AddSingleton<IBettingLoggingService, BettingLoggingService>();

        _serviceProvider = services.BuildServiceProvider();
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BettingDbContext>();
            db.Database.EnsureCreated();
            // BetRepository.GetByIdAsync does .Include(b => b.User) — seed both users
            // so bets have a real row to join against, matching production (a bet's
            // UserId always comes from an authenticated, already-registered user).
            db.Users.Add(new Data.Entities.User { Id = UserA, Username = "userA", Email = "a@test.local", PasswordHash = "x" });
            db.Users.Add(new Data.Entities.User { Id = UserB, Username = "userB", Email = "b@test.local", PasswordHash = "x" });
            db.SaveChanges();
        }

        _service = _serviceProvider.GetRequiredService<IBettingLoggingService>();
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static BetHistory Bet(string matchId, double probability, string result, decimal stake = 100m) => new()
    {
        MatchId = matchId, HomeTeam = "A", AwayTeam = "B", Team = "A", Outcome = "Home",
        Odds = 2.0m, Probability = probability, Edge = 0.05, Stake = stake,
        Result = result, SportType = SportType.EPL,
    };

    [Fact]
    public async Task GetHistoryAsync_OnlyReturnsCallingUsersOwnBets()
    {
        await _service.LogBetAsync(UserA, Bet("M1", 0.6, "Win"));
        await _service.LogBetAsync(UserB, Bet("M2", 0.6, "Win"));

        var historyA = await _service.GetHistoryAsync(UserA);

        historyA.Should().ContainSingle(b => b.MatchId == "M1");
        historyA.Should().NotContain(b => b.MatchId == "M2");
    }

    [Fact]
    public async Task GetByIdAsync_ForAnotherUsersBet_ReturnsNull()
    {
        var bet = Bet("M1", 0.6, "Pending");
        await _service.LogBetAsync(UserA, bet);

        var asOwner   = await _service.GetByIdAsync(bet.Id, UserA);
        var asStranger = await _service.GetByIdAsync(bet.Id, UserB);

        asOwner.Should().NotBeNull();
        asStranger.Should().BeNull("a user must not be able to look up another user's bet by GUID");
    }

    [Fact]
    public async Task UpdateResultAsync_ForAnotherUsersBet_IsANoOp()
    {
        var bet = Bet("M1", 0.6, "Pending");
        await _service.LogBetAsync(UserA, bet);

        await _service.UpdateResultAsync(bet.Id, UserB, "Win", 100m, null, null);

        var stillOwnedByA = await _service.GetByIdAsync(bet.Id, UserA);
        stillOwnedByA!.Result.Should().Be("Pending", "another user's update attempt must not change this bet's result");
    }

    [Fact]
    public async Task GetCalibrationReportAsync_BucketsByPredictedProbabilityDecile()
    {
        // Bucket 50-60%: two bets at 0.55, both settle as Win -> 100% actual vs ~55% predicted
        await _service.LogBetAsync(UserA, Bet("M1", 0.55, "Win"));
        await _service.LogBetAsync(UserA, Bet("M2", 0.55, "Win"));
        // Bucket 80-90%: one bet at 0.85, settles as Loss -> 0% actual vs 85% predicted
        await _service.LogBetAsync(UserA, Bet("M3", 0.85, "Loss"));

        var report = await _service.GetCalibrationReportAsync(UserA);

        report.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCalibrationReportAsync_IsScopedPerUser()
    {
        await _service.LogBetAsync(UserA, Bet("M1", 0.55, "Win"));
        await _service.LogBetAsync(UserB, Bet("M2", 0.85, "Loss"));

        var reportA = await _service.GetCalibrationReportAsync(UserA);

        reportA.Should().HaveCount(1, "only UserA's single 50-60% bucket bet should appear");
    }
}
