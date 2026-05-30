using BettingAnalysis.Data;
using BettingAnalysis.Data.Repositories;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BettingAnalysis.Tests.Services;

public class BankrollServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IBankrollService _service;

    public BankrollServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BettingSettings:InitialBankroll"]       = "10000",
                ["BettingSettings:MaxStakePercent"]       = "0.03",
                ["BettingSettings:DailyLossLimitPercent"] = "0.10",
                ["BettingSettings:StopLossPercent"]       = "0.20",
                ["BettingSettings:MaxExposurePercent"]    = "0.10",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var dbName = $"BankrollTestDb_{Guid.NewGuid()}";
        services.AddDbContext<BettingDbContext>(o => o.UseInMemoryDatabase(dbName));

        services.AddScoped<IBetRepository, BetRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBankrollSnapshotRepository, BankrollSnapshotRepository>();

        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddSingleton<IBankrollService, BankrollService>();

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<BettingDbContext>().Database.EnsureCreated();
        }

        _service = _serviceProvider.GetRequiredService<IBankrollService>();
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task GetBankroll_InitialState_ReturnsSeedAmount()
    {
        var b = await _service.GetBankrollAsync();

        b.TotalBankroll.Should().Be(10_000m);
        b.AvailableBankroll.Should().Be(10_000m);
        b.DailyLossUsed.Should().Be(0m);
        b.CumulativeLoss.Should().Be(0m);
    }

    [Fact]
    public async Task ReserveStake_ReducesAvailableBankroll()
    {
        await _service.ReserveStakeAsync(200m);

        var b = await _service.GetBankrollAsync();
        b.AvailableBankroll.Should().Be(9_800m);
        b.TotalBankroll.Should().Be(10_000m);
    }

    [Fact]
    public async Task UpdateAfterResult_Win_IncreasesTotalBankroll()
    {
        await _service.ReserveStakeAsync(100m);
        await _service.UpdateAfterResultAsync(100m, 2.50m, "Win");

        var b = await _service.GetBankrollAsync();
        // profit = 100 * (2.5 - 1) = 150; total = 10000 + 150 = 10150
        b.TotalBankroll.Should().Be(10_150m);
        b.AvailableBankroll.Should().Be(10_150m);
    }

    [Fact]
    public async Task UpdateAfterResult_Loss_ReducesTotalAndTracksDailyLoss()
    {
        await _service.ReserveStakeAsync(100m);
        await _service.UpdateAfterResultAsync(100m, 2.50m, "Loss");

        var b = await _service.GetBankrollAsync();
        b.TotalBankroll.Should().Be(9_900m);
        b.DailyLossUsed.Should().Be(100m);
        b.CumulativeLoss.Should().Be(100m);
    }

    [Fact]
    public async Task IsDailyLimitReached_WhenDailyLossExceeds10Pct()
    {
        for (int i = 0; i < 11; i++)
        {
            await _service.ReserveStakeAsync(100m);
            await _service.UpdateAfterResultAsync(100m, 2.0m, "Loss");
        }

        var b = await _service.GetBankrollAsync();
        b.IsDailyLimitReached.Should().BeTrue();
    }

    [Fact]
    public async Task IsStopLossTriggered_WhenCumulativeLossExceeds20Pct()
    {
        for (int i = 0; i < 22; i++)
        {
            await _service.ReserveStakeAsync(100m);
            await _service.UpdateAfterResultAsync(100m, 2.0m, "Loss");
        }

        var b = await _service.GetBankrollAsync();
        b.IsStopLossTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_WithNewAmount_RestoresAllCounters()
    {
        await _service.ReserveStakeAsync(100m);
        await _service.UpdateAfterResultAsync(100m, 2.0m, "Loss");

        await _service.ResetAsync(5_000m);

        var b = await _service.GetBankrollAsync();
        b.TotalBankroll.Should().Be(5_000m);
        b.AvailableBankroll.Should().Be(5_000m);
        b.DailyLossUsed.Should().Be(0m);
        b.CumulativeLoss.Should().Be(0m);
    }

    [Fact]
    public async Task Reset_WithoutAmount_UsesInitialBankroll()
    {
        await _service.ReserveStakeAsync(300m);
        await _service.UpdateAfterResultAsync(300m, 2.0m, "Loss");

        await _service.ResetAsync();

        var b = await _service.GetBankrollAsync();
        b.TotalBankroll.Should().Be(10_000m);
    }

    [Fact]
    public async Task MaxStakePerBet_IsThreePercentOfTotalBankroll()
    {
        var b = await _service.GetBankrollAsync();
        b.MaxStakePerBet.Should().Be(300m);
    }
}
