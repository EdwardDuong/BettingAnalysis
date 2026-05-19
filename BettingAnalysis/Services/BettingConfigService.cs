using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Singleton that holds live betting configuration.
/// Seeded from appsettings.json on startup.
/// Updated at runtime via PUT /Betting/settings — changes apply immediately.
/// </summary>
public class BettingConfigService
{
    private BettingConfig _config;
    private readonly object _lock = new();

    public BettingConfigService(IConfiguration cfg)
    {
        _config = new BettingConfig
        {
            EdgeThreshold          = cfg.GetValue<double>("BettingSettings:EdgeThreshold", 0.05),
            HighEdgeThreshold      = cfg.GetValue<double>("BettingSettings:HighEdgeThreshold", 0.20),
            KellyFraction          = cfg.GetValue<double>("BettingSettings:KellyFraction", 0.5),
            MaxStakePercent        = cfg.GetValue<double>("BettingSettings:MaxStakePercent", 0.03),
            DailyLossLimitPercent  = cfg.GetValue<double>("BettingSettings:DailyLossLimitPercent", 0.10),
            StopLossPercent        = cfg.GetValue<double>("BettingSettings:StopLossPercent", 0.20),
            MaxExposurePercent     = cfg.GetValue<double>("BettingSettings:MaxExposurePercent", 0.10),
            ParlayMinEdge          = cfg.GetValue<double>("BettingSettings:ParlayMinEdge", 0.02),
            PreMatchMinHours       = cfg.GetValue<double>("BettingSettings:PreMatchMinHoursAhead", 1.0),
            PreMatchMaxHours       = cfg.GetValue<double>("BettingSettings:PreMatchMaxHoursAhead", 336.0),
            MaxConsecutiveLosses   = cfg.GetValue<int>("BettingSettings:MaxConsecutiveLosses", 3),
            MaxBetsPerMatch        = cfg.GetValue<int>("BettingSettings:MaxBetsPerMatch", 2),
            TeamBlacklist          = cfg.GetSection("BettingSettings:TeamBlacklist").Get<List<string>>() ?? new(),
            RequireLineMovementCheck = true,
        };
    }

    public BettingConfig Get() { lock (_lock) return _config; }

    public void Update(BettingConfig updated) { lock (_lock) _config = updated; }
}
