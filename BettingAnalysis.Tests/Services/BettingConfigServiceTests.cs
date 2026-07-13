using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// BettingConfigService previously had zero test coverage despite seeding every
/// risk-management rule's runtime value from appsettings.json. This covers the
/// seeding (including its fallback defaults) and the runtime Update() path used by
/// PUT /Betting/settings.
/// </summary>
public class BettingConfigServiceTests
{
    private static IBettingConfigService BuildService(Dictionary<string, string?>? overrides = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build();

        return new BettingConfigService(configuration);
    }

    [Fact]
    public void Get_WhenNoConfigurationProvided_UsesDocumentedDefaults()
    {
        var service = BuildService();

        var config = service.Get();

        config.EdgeThreshold.Should().Be(0.05);
        config.HighEdgeThreshold.Should().Be(0.20);
        config.KellyFraction.Should().Be(0.5);
        config.MaxStakePercent.Should().Be(0.03);
        config.DailyLossLimitPercent.Should().Be(0.10);
        config.StopLossPercent.Should().Be(0.20);
        config.MaxExposurePercent.Should().Be(0.10);
        config.MaxConsecutiveLosses.Should().Be(3);
        config.MaxBetsPerMatch.Should().Be(2);
        config.TeamBlacklist.Should().BeEmpty();
    }

    [Fact]
    public void Get_WhenAppSettingsOverridesProvided_SeedsFromConfiguration()
    {
        var service = BuildService(new Dictionary<string, string?>
        {
            ["BettingSettings:EdgeThreshold"] = "0.08",
            ["BettingSettings:MaxStakePercent"] = "0.05",
            ["BettingSettings:MaxConsecutiveLosses"] = "5",
            ["BettingSettings:TeamBlacklist:0"] = "Injury-Riddled FC",
        });

        var config = service.Get();

        config.EdgeThreshold.Should().Be(0.08);
        config.MaxStakePercent.Should().Be(0.05);
        config.MaxConsecutiveLosses.Should().Be(5);
        config.TeamBlacklist.Should().ContainSingle().Which.Should().Be("Injury-Riddled FC");
    }

    [Fact]
    public void Update_ReplacesConfig_SoSubsequentGetReturnsNewValues()
    {
        var service = BuildService();
        var updated = new BettingConfig { EdgeThreshold = 0.12, MaxStakePercent = 0.04 };

        service.Update(updated);

        var config = service.Get();
        config.EdgeThreshold.Should().Be(0.12);
        config.MaxStakePercent.Should().Be(0.04);
    }

    [Fact]
    public void Update_IsVisibleAcrossConcurrentReaders()
    {
        var service = BuildService();
        var updated = new BettingConfig { EdgeThreshold = 0.99 };

        Parallel.For(0, 50, _ => service.Get());
        service.Update(updated);

        service.Get().EdgeThreshold.Should().Be(0.99);
    }
}
