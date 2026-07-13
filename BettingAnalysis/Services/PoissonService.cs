using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Poisson Distribution Match Outcome Model.
///
/// For sports with draws (soccer leagues — see SportTypeExtensions.IsSoccerLeague):
///   Each team's goals are modelled as independent Poisson processes.
///   P(team scores k goals) = e^(-lambda) * lambda^k / k!
///   We enumerate all score combinations 0..MaxGoals x 0..MaxGoals
///   and accumulate home win / draw / away win probabilities.
///
/// For sports without draws (AFL, NRL, NBA, MLB, Esports):
///   Simplified relative-strength model using the same lambdas
///   as a proxy for team quality (no draw bucket).
///
/// Lambdas are calibrated against historical data in production.
/// In the mock, they are hand-set per team pair.
/// </summary>
public class PoissonService : IPoissonService
{
    /// <summary>
    /// Maximum goals/scores to enumerate per team.
    /// P(X > 10 | lambda=2.5) ≈ 0.002 — safe truncation point.
    /// </summary>
    private const int MaxGoals = 10;

    public PredictionResult Predict(MatchOdds match) =>
        match.SportType.IsSoccerLeague()
            ? PredictWithPoisson(match.HomeLambda, match.AwayLambda) // full Poisson grid with draw bucket
            : PredictNoDraw(match.HomeLambda, match.AwayLambda);     // binary outcome, no draw

    /// <summary>
    /// Full Poisson model: enumerate score matrix and classify results.
    /// Probabilities are renormalized to correct for MaxGoals truncation.
    /// </summary>
    private static PredictionResult PredictWithPoisson(double homeLambda, double awayLambda)
    {
        double homeWin = 0, draw = 0, awayWin = 0;

        for (int h = 0; h <= MaxGoals; h++)
        {
            for (int a = 0; a <= MaxGoals; a++)
            {
                double prob = PoissonPmf(h, homeLambda) * PoissonPmf(a, awayLambda);
                if (h > a)      homeWin += prob;
                else if (h == a) draw   += prob;
                else             awayWin += prob;
            }
        }

        // Renormalize (tiny probability mass is truncated beyond MaxGoals)
        double total = homeWin + draw + awayWin;
        return new PredictionResult
        {
            HomeWinProb = homeWin / total,
            DrawProb    = draw    / total,
            AwayWinProb = awayWin / total
        };
    }

    /// <summary>
    /// Binary model for no-draw sports.
    /// Win probability proportional to relative lambda (strength ratio).
    /// Home advantage is already baked into the HomeLambda calibration.
    /// </summary>
    private static PredictionResult PredictNoDraw(double homeLambda, double awayLambda)
    {
        double total = homeLambda + awayLambda;
        if (total <= 0)
            return new PredictionResult { HomeWinProb = 0.5, DrawProb = 0, AwayWinProb = 0.5 };

        double homeWin  = homeLambda / total;
        double awayWin  = awayLambda / total;
        return new PredictionResult
        {
            HomeWinProb = homeWin,
            DrawProb    = 0,
            AwayWinProb = awayWin
        };
    }

    /// <summary>
    /// Poisson PMF: P(X = k | lambda) = e^(-lambda) * lambda^k / k!
    /// </summary>
    private static double PoissonPmf(int k, double lambda)
    {
        return Math.Exp(-lambda) * Math.Pow(lambda, k) / Factorial(k);
    }

    private static double Factorial(int n)
    {
        double result = 1;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }
}
