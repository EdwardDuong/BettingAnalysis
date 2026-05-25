namespace BettingAnalysis.Interfaces;

public interface ICLVService
{
    double CalculateCLV(decimal placedOdds, decimal closingOdds);
    string Interpret(double clv);
    string GetColour(double clv);
}
