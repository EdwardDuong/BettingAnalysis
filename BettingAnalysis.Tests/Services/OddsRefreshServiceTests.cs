using System.Reflection;
using BettingAnalysis.Hubs;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// OddsRefreshService previously had zero test coverage even though it's the only
/// thing that invalidates the shared odds cache and tells connected clients to
/// re-fetch. The production refresh interval is minutes-granularity and configured
/// via IConfiguration, so a "0" override would collapse Task.Delay to TimeSpan.Zero
/// and spin the background loop as fast as the CPU allows — that reliably crashed
/// the test host (unbounded Moq invocation growth). Instead the private _interval
/// field is overridden via reflection to a short-but-nonzero duration after
/// construction, which exercises the same loop body without the tight spin.
/// </summary>
public class OddsRefreshServiceTests
{
    private static readonly TimeSpan TestInterval = TimeSpan.FromMilliseconds(20);

    private readonly Mock<IOddsService> _oddsMock = new();
    private readonly Mock<IClientProxy> _clientProxyMock = new();
    private readonly Mock<IHubClients> _hubClientsMock = new();
    private readonly Mock<IHubContext<BettingHub>> _hubContextMock = new();

    public OddsRefreshServiceTests()
    {
        _hubClientsMock.Setup(c => c.All).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(_hubClientsMock.Object);
    }

    private OddsRefreshService BuildService()
    {
        var configuration = new ConfigurationBuilder().Build();

        var service = new OddsRefreshService(
            _oddsMock.Object,
            _hubContextMock.Object,
            configuration,
            NullLogger<OddsRefreshService>.Instance);

        typeof(OddsRefreshService)
            .GetField("_interval", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, TestInterval);

        return service;
    }

    [Fact]
    public async Task ExecuteAsync_InvalidatesOddsCache_AndBroadcastsOddsRefreshed()
    {
        var service = BuildService();

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => _oddsMock.Invocations.Count > 0);
        await service.StopAsync(CancellationToken.None);

        _oddsMock.Verify(o => o.InvalidateCache(), Times.AtLeastOnce);
        _clientProxyMock.Verify(c => c.SendCoreAsync(
            "OddsRefreshed",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_StopsRefreshing_WhenCancelled()
    {
        var service = BuildService();

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => _oddsMock.Invocations.Count > 0);
        await service.StopAsync(CancellationToken.None);

        var invocationsAtStop = _oddsMock.Invocations.Count;
        await Task.Delay(50);

        _oddsMock.Invocations.Count.Should().Be(invocationsAtStop,
            "the background loop must not keep running after StopAsync cancels it");
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        condition().Should().BeTrue("condition did not become true within the timeout");
    }
}
