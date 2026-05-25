using BettingAnalysis.Interfaces;

namespace BettingAnalysis.Services;

/// <summary>
/// Closing Line Value (CLV) Service.
///
/// CLV is the single most reliable long-term profitability indicator.
/// If you consistently beat the closing line, variance will resolve in your favour.
///
/// CLV formula:
///   CLV% = (PlacedOdds / ClosingOdds − 1) × 100
///
/// Example:
///   You placed at 2.20, closing odds were 2.00
///   CLV = (2.20 / 2.00 − 1) × 100 = +10% ✅  (you got 10% better odds than closing)
///
///   You placed at 1.90, closing odds were 2.10
///   CLV = (1.90 / 2.10 − 1) × 100 = −9.5% ❌  (you got worse odds than closing)
///
/// Industry benchmarks:
///   CLV > +5%  = Excellent sharp bettor
///   CLV > +2%  = Good, consistently profitable
///   CLV ~  0%  = Break-even (edge eaten by vig over time)
///   CLV < −2%  = Model is wrong or timing is poor — review required
/// </summary>
public class CLVService : ICLVService
{
    public double CalculateCLV(decimal placedOdds, decimal closingOdds)
    {
        if (closingOdds <= 0) return 0;
        return ((double)placedOdds / (double)closingOdds - 1.0) * 100.0;
    }

    public string Interpret(double clv) => clv switch
    {
        >= 5  => "Excellent",
        >= 2  => "Good",
        >= 0  => "Marginal",
        >= -2 => "Warning",
        _     => "Poor — review model"
    };

    public string GetColour(double clv) => clv switch
    {
        >= 2  => "green",
        >= 0  => "yellow",
        _     => "red"
    };
}
