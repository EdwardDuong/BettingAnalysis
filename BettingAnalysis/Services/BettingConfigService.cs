using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Singleton that holds live betting configuration.
/// Seeded from appsettings.json on startup.
/// Updated at runtime via PUT /Betting/settings — changes apply immediately.
/// </summary>
public class BettingConfigService : IBettingConfigService
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
            GoodBetMaxStake        = cfg.GetValue<decimal>("BettingSettings:GoodBetMaxStake", 500m),
            RiskyMaxStake          = cfg.GetValue<decimal>("BettingSettings:RiskyMaxStake", 50m),
            Parlay3MaxStake        = cfg.GetValue<decimal>("BettingSettings:Parlay3MaxStake", 100m),
            Parlay4MaxStake        = cfg.GetValue<decimal>("BettingSettings:Parlay4MaxStake", 75m),
            Parlay5MaxStake        = cfg.GetValue<decimal>("BettingSettings:Parlay5MaxStake", 50m),
            TeamBlacklist          = cfg.GetSection("BettingSettings:TeamBlacklist").Get<List<string>>() ?? new(),
            RequireLineMovementCheck = true,
            DailyDoubleTargetOdds   = cfg.GetValue<decimal>("BettingSettings:DailyDoubleTargetOdds", 2.0m),
            DailyDoubleMaxLegs      = cfg.GetValue<int>("BettingSettings:DailyDoubleMaxLegs", 20),
            DailyDoubleMaxStake     = cfg.GetValue<decimal>("BettingSettings:DailyDoubleMaxStake", 100m),
            BigMatchupEdgeThreshold = cfg.GetValue<double>("BettingSettings:BigMatchupEdgeThreshold", 0.08),
            SoccerCalibrationShrinkage = cfg.GetValue<double>("BettingSettings:SoccerCalibrationShrinkage", 0.5),
            // HomeCalibration and BigTeams are intentionally NOT read from appsettings.json —
            // they're nested per-sport dictionaries best edited via PUT /Betting/settings or
            // in code; the class-level defaults in BettingConfig.cs apply unless overridden live.
        };
    }

    public BettingConfig Get() { lock (_lock) return _config; }

    public void Update(BettingConfig updated) { lock (_lock) _config = updated; }
}
