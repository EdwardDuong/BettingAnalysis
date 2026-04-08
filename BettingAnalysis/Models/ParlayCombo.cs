namespace BettingAnalysis.Models;

/// <summary>
/// A recommended multi-leg accumulator built from GOOD_BET opportunities.
/// Combined odds = product of all leg odds.
/// Combined probability = product of all leg win probabilities (assumes independence).
/// Combined edge = (combinedOdds × combinedProb) − 1.
/// </summary>
public class ParlayCombo
{
    public int    Legs            { get; set; }
    public string RiskLabel       { get; set; } = string.Empty;  // "Safe" | "Medium" | "Aggressive" | "Extreme"
    public decimal CombinedOdds  { get; set; }
    public double  CombinedProb  { get; set; }   // 0–1
    public double  ExpectedValue { get; set; }   // (combinedOdds × combinedProb) − 1
    public double  AvgEdge       { get; set; }   // Average leg edge
    public decimal SuggestedStake { get; set; }  // Half-Kelly on the combo
    public double  AvgAiScore    { get; set; }
    public List<ParlayLeg> Selections { get; set; } = new();
}

public class ParlayLeg
{
    public string  MatchId    { get; set; } = string.Empty;
    public string  HomeTeam   { get; set; } = string.Empty;
    public string  AwayTeam   { get; set; } = string.Empty;
    public string  Team       { get; set; } = string.Empty;
    public string  Outcome    { get; set; } = string.Empty;
    public decimal Odds       { get; set; }
    public double  Probability { get; set; }
    public double  Edge       { get; set; }
    public string  SportType  { get; set; } = string.Empty;
    public string  LineMovement { get; set; } = string.Empty;
    public double  AiScore    { get; set; }
    public string  AiDecision { get; set; } = string.Empty;
    public DateTime KickoffTime { get; set; }
}
