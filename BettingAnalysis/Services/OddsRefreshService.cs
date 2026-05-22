using BettingAnalysis.Hubs;
using BettingAnalysis.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BettingAnalysis.Services;

/// <summary>
/// Background service that invalidates the odds cache on a fixed interval
/// and broadcasts an "OddsRefreshed" event via SignalR so connected clients
/// know to re-fetch opportunities without polling.
/// </summary>
public class OddsRefreshService : BackgroundService
{
    private readonly IOddsService                 _odds;
    private readonly IHubContext<BettingHub>      _hub;
    private readonly ILogger<OddsRefreshService>  _logger;
    private readonly TimeSpan                     _interval;

    public OddsRefreshService(
        IOddsService                odds,
        IHubContext<BettingHub>     hub,
        IConfiguration              config,
        ILogger<OddsRefreshService> logger)
    {
        _odds     = odds;
        _hub      = hub;
        _logger   = logger;
        _interval = TimeSpan.FromMinutes(config.GetValue<int>("BettingSettings:OddsRefreshMinutes", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("OddsRefreshService started — refreshing every {Min} min", _interval.TotalMinutes);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_interval, ct);

            _odds.InvalidateCache();
            _logger.LogInformation("Odds cache invalidated at {Time:HH:mm} UTC", DateTime.UtcNow);

            // Notify all connected clients that odds have refreshed
            await _hub.Clients.All.SendAsync("OddsRefreshed", new
            {
                Timestamp = DateTime.UtcNow,
                Message   = "Odds cache refreshed — reload /Betting/opportunities"
            }, ct);
        }
    }
}
