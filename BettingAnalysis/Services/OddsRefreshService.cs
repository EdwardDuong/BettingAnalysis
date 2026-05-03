namespace BettingAnalysis.Services;

/// <summary>
/// Background service that invalidates the odds cache on a fixed interval
/// so the next request to /opportunities always fetches fresh data.
/// Interval defaults to 30 minutes; override via BettingSettings:OddsRefreshMinutes.
/// </summary>
public class OddsRefreshService : BackgroundService
{
    private readonly OddsService                  _odds;
    private readonly ILogger<OddsRefreshService>  _logger;
    private readonly TimeSpan                     _interval;

    public OddsRefreshService(OddsService odds, IConfiguration config, ILogger<OddsRefreshService> logger)
    {
        _odds     = odds;
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
            _logger.LogInformation("Odds cache invalidated by background refresh at {Time:HH:mm} UTC", DateTime.UtcNow);
        }
    }
}
