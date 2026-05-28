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
    public void GetBankroll_InitialState_ReturnsSeedAmount()
    {
        var b = _service.GetBankroll();

        b.TotalBankroll.Should().Be(10_000m);
        b.AvailableBankroll.Should().Be(10_000m);
        b.DailyLossUsed.Should().Be(0m);
        b.CumulativeLoss.Should().Be(0m);
    }

    [Fact]
    public void ReserveStake_ReducesAvailableBankroll()
    {
        _service.ReserveStake(200m);

        var b = _service.GetBankroll();
        b.AvailableBankroll.Should().Be(9_800m);
        b.TotalBankroll.Should().Be(10_000m);  // total unchanged until result
    }

    [Fact]
    public void UpdateAfterResult_Win_IncreasesTotalBankroll()
    {
        _service.ReserveStake(100m);
        _service.UpdateAfterResult(100m, 2.50m, "Win");

        var b = _service.GetBankroll();
        // profit = 100 * (2.5 - 1) = 150; total = 10000 + 150 = 10150
        b.TotalBankroll.Should().Be(10_150m);
        b.AvailableBankroll.Should().Be(10_150m);
    }

    [Fact]
    public void UpdateAfterResult_Loss_ReducesTotalAndTracksDailyLoss()
    {
        _service.ReserveStake(100m);
        _service.UpdateAfterResult(100m, 2.50m, "Loss");

        var b = _service.GetBankroll();
        b.TotalBankroll.Should().Be(9_900m);
        b.DailyLossUsed.Should().Be(100m);
        b.CumulativeLoss.Should().Be(100m);
    }

    [Fact]
    public void IsDailyLimitReached_WhenDailyLossExceeds10Pct()
    {
        // 10% of 10000 = 1000; losing 1050 should trigger
        for (int i = 0; i < 11; i++)   // 11 × $100 = $1100
        {
            _service.ReserveStake(100m);
            _service.UpdateAfterResult(100m, 2.0m, "Loss");
        }

        var b = _service.GetBankroll();
        b.IsDailyLimitReached.Should().BeTrue();
    }

    [Fact]
    public void IsStopLossTriggered_WhenCumulativeLossExceeds20Pct()
    {
        // 20% of 10000 = 2000; losing 2100 should trigger
        for (int i = 0; i < 22; i++)   // 22 × $100 = $2200
        {
            _service.ReserveStake(100m);
            _service.UpdateAfterResult(100m, 2.0m, "Loss");
        }

        var b = _service.GetBankroll();
        b.IsStopLossTriggered.Should().BeTrue();
    }

    [Fact]
    public void Reset_WithNewAmount_RestoresAllCounters()
    {
        _service.ReserveStake(100m);
        _service.UpdateAfterResult(100m, 2.0m, "Loss");

        _service.Reset(5_000m);

        var b = _service.GetBankroll();
        b.TotalBankroll.Should().Be(5_000m);
        b.AvailableBankroll.Should().Be(5_000m);
        b.DailyLossUsed.Should().Be(0m);
        b.CumulativeLoss.Should().Be(0m);
    }

    [Fact]
    public void Reset_WithoutAmount_UsesInitialBankroll()
    {
        _service.ReserveStake(300m);
        _service.UpdateAfterResult(300m, 2.0m, "Loss");

        _service.Reset();

        var b = _service.GetBankroll();
        b.TotalBankroll.Should().Be(10_000m);
    }

    [Fact]
    public void MaxStakePerBet_IsThreePercentOfTotalBankroll()
    {
        var b = _service.GetBankroll();
        b.MaxStakePerBet.Should().Be(300m);  // 3% × 10000
    }
}
