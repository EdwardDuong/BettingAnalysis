using BettingAnalysis.Interfaces;

namespace BettingAnalysis.Services;

/// <summary>
/// Half-Kelly bet sizing — uses live BettingConfigService so changes in SettingsPanel apply instantly.
/// </summary>
public class BetSizingService : IBetSizingService
{
    private readonly IBettingConfigService _cfg;

    public BetSizingService(IBettingConfigService cfg) => _cfg = cfg;

    public decimal CalculateStake(double probability, decimal odds, decimal bankroll)
    {
        var config = _cfg.Get();
        double b = (double)odds - 1.0;
        double q = 1.0 - probability;
        double fullKelly = (probability * b - q) / b;

        double fraction = fullKelly * config.KellyFraction;
        fraction = Math.Max(0, fraction);
        fraction = Math.Min(fraction, config.MaxStakePercent);

        return Math.Round(bankroll * (decimal)fraction, 2);
    }
}
