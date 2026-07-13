namespace BettingAnalysis.Models;

/// <summary>
/// Live-editable configuration for all betting rules.
/// Updated via PUT /Betting/settings — changes apply immediately without restart.
/// Initial values are seeded from appsettings.json by BettingConfigService.
/// </summary>
public class BettingConfig
{
    // ── Edge rules ────────────────────────────────────────────────────────────
    /// <summary>Rule #2: Minimum edge to show/place a bet (default 5%).</summary>
    public double EdgeThreshold { get; set; } = 0.05;

    /// <summary>Rule #3: Edge above this triggers manual verification warning (default 20%).</summary>
    public double HighEdgeThreshold { get; set; } = 0.20;

    // ── Sizing ────────────────────────────────────────────────────────────────
    /// <summary>Fractional Kelly multiplier. 0.5 = half-Kelly (recommended).</summary>
    public double KellyFraction { get; set; } = 0.5;

    /// <summary>Rule: Hard cap on stake as fraction of bankroll (default 3%).</summary>
    public double MaxStakePercent { get; set; } = 0.03;

    /// <summary>Dollar cap for GOOD_BET decisions. Kelly may suggest less; this is the ceiling.</summary>
    public decimal GoodBetMaxStake { get; set; } = 500m;

    /// <summary>Dollar cap for RISKY decisions. Keeps exposure low on less certain bets.</summary>
    public decimal RiskyMaxStake { get; set; } = 50m;

    /// <summary>Dollar cap for 3-leg parlay (Safe tier).</summary>
    public decimal Parlay3MaxStake { get; set; } = 100m;

    /// <summary>Dollar cap for 4-leg parlay (Medium tier).</summary>
    public decimal Parlay4MaxStake { get; set; } = 75m;

    /// <summary>Dollar cap for 5-leg parlay (Aggressive tier).</summary>
    public decimal Parlay5MaxStake { get; set; } = 50m;

    // ── Bankroll limits ───────────────────────────────────────────────────────
    /// <summary>Rule: Stop betting for the day if daily loss exceeds this fraction (default 10%).</summary>
    public double DailyLossLimitPercent { get; set; } = 0.10;

    /// <summary>Rule: Halt system entirely if cumulative drawdown exceeds this fraction (default 20%).</summary>
    public double StopLossPercent { get; set; } = 0.20;

    /// <summary>Rule: Total open bet exposure must not exceed this fraction of bankroll (default 10%).</summary>
    public double MaxExposurePercent { get; set; } = 0.10;

    /// <summary>
    /// Minimum edge for a leg to be eligible for parlay inclusion (default 2%).
    /// Lower than EdgeThreshold so short-priced legs can anchor multi-leg combos.
    /// </summary>
    public double ParlayMinEdge { get; set; } = 0.02;

    // ── Timing window ─────────────────────────────────────────────────────────
    /// <summary>Rule: Do not bet less than this many hours before kickoff (default 1h).</summary>
    public double PreMatchMinHours { get; set; } = 1.0;

    /// <summary>Rule: Do not bet more than this many hours before kickoff (default 336h = 2 weeks).</summary>
    public double PreMatchMaxHours { get; set; } = 336.0;

    // ── Tilt protection ───────────────────────────────────────────────────────
    /// <summary>Rule: Halt betting after this many consecutive losses (default 3).</summary>
    public int MaxConsecutiveLosses { get; set; } = 3;

    // ── Correlation / exposure per match ──────────────────────────────────────
    /// <summary>Rule: Max simultaneous bets on the same match (default 2).</summary>
    public int MaxBetsPerMatch { get; set; } = 2;

    // ── Emotional bias protection ─────────────────────────────────────────────
    /// <summary>Rule: Teams on this list are never bet on regardless of edge.</summary>
    public List<string> TeamBlacklist { get; set; } = new();

    // ── Line movement ─────────────────────────────────────────────────────────
    /// <summary>Rule: Block bets when odds are drifting against prediction.</summary>
    public bool RequireLineMovementCheck { get; set; } = true;

    // ── Market focus (Rule #6) ────────────────────────────────────────────────
    /// <summary>
    /// Minimum edge required for a match between two "big" teams (see BigTeams) in
    /// the same league. These are the most heavily-bet, most efficiently-priced
    /// matchups, so a thin edge there is more likely to be model noise than real
    /// value — below this, AIValidatorService adds ValidationFlags.BigMatchupLowEdge
    /// and subtracts 1 from the score. This is a soft penalty, not a hard block.
    /// </summary>
    public double BigMatchupEdgeThreshold { get; set; } = 0.08;

    /// <summary>
    /// Per-league list of "big" teams. A match only counts as a "big matchup" for
    /// BigMatchupEdgeThreshold if BOTH HomeTeam and AwayTeam are in that league's
    /// list (case-insensitive exact match). Hand-curated, not derived from betting
    /// volume/handle data — this system has no access to real market liquidity
    /// figures, so treat this the same as TeamBlacklist: a manually maintained list,
    /// not a computed one.
    /// </summary>
    public Dictionary<SportType, List<string>> BigTeams { get; set; } = new()
    {
        [SportType.EPL] = new()
            { "Manchester City", "Liverpool", "Arsenal", "Manchester United", "Chelsea", "Tottenham" },
        [SportType.LaLiga]       = new() { "Real Madrid", "Barcelona", "Atletico Madrid" },
        [SportType.Bundesliga]   = new() { "Bayern Munich", "Borussia Dortmund" },
        [SportType.SerieA]       = new() { "Juventus", "Inter Milan", "AC Milan", "Napoli", "Atalanta" },
        [SportType.Ligue1]       = new() { "Paris Saint-Germain", "Marseille" },
        [SportType.Eredivisie]   = new() { "Ajax", "PSV Eindhoven", "Feyenoord" },
        [SportType.PrimeiraLiga] = new() { "Benfica", "Porto", "Sporting CP" },
        // MLS and ChampionsLeague deliberately omitted: MLS markets aren't
        // efficiently-priced the way top-5-European-league markets are, and nearly
        // every Champions League participant would count as "big", making the
        // filter meaningless for that competition.
    };
    // NOT YET VERIFIED against real Odds API responses: the match against
    // HomeTeam/AwayTeam is an exact (case-insensitive) string comparison, and
    // these names were typed by hand to match this app's own mock-data
    // conventions (e.g. "Tottenham", not "Tottenham Hotspur"). Every league this
    // list covers is European and out of season as of writing (~June-July), so
    // the real API returns zero matches for all of them right now -- there has
    // been no opportunity to confirm these strings match what the live API
    // actually sends. Check this against real fixtures once the season resumes
    // (~August) before trusting Rule #6 in production; if names don't match, the
    // rule silently never fires (no error, no log) rather than failing loudly.

    // ── Daily double-up pick ──────────────────────────────────────────────────
    /// <summary>Target combined odds for the daily "safest way to double" pick.</summary>
    public decimal DailyDoubleTargetOdds { get; set; } = 2.0m;

    /// <summary>Max legs the daily double-up pick will combine before giving up.</summary>
    public int DailyDoubleMaxLegs { get; set; } = 20;

    /// <summary>Dollar cap for the daily double-up pick's suggested stake.</summary>
    public decimal DailyDoubleMaxStake { get; set; } = 100m;

    // ── Soccer calibration stability ──────────────────────────────────────────
    /// <summary>
    /// Shrinkage applied to the soccer lambda calibration's ratio-to-league-average
    /// scaling (see TheOddsApiService.MapToMatchOdds / DampenRatio). The raw ratio
    /// (this match's market-implied win prob ÷ the league's average win rate)
    /// amplifies deviation instead of damping it — the further a match's odds sit
    /// from the league average (i.e. exactly the lopsided favourite/underdog
    /// matchups), the more the model exaggerates that deviation, compounding
    /// whatever error is already in SoccerParams rather than absorbing it.
    ///
    /// 1.0 = full raw ratio (old, undamped behaviour). 0.0 = ignore this match's own
    /// market odds entirely and always predict the league-average lambda (edge is
    /// then always 0, by construction). Default 0.5 deliberately favours plausible,
    /// consistent predictions over occasionally-large-but-unreliable edge — these
    /// calibration constants are unverified placeholders (see HomeCalibration's doc
    /// comment), so being roughly right consistently matters more than being
    /// dramatically "right" on a guess. Lower this further (toward 0) if
    /// GET /Betting/stats/calibration shows soccer edge is systematically overconfident.
    /// </summary>
    public double SoccerCalibrationShrinkage { get; set; } = 0.5;

    // ── Non-soccer home-advantage calibration ─────────────────────────────────
    /// <summary>
    /// Home-advantage calibration multiplier per non-soccer sport, applied by
    /// TheOddsApiService to the de-vigged market-implied win probability to produce
    /// the model's probability. Without this, model probability equals the de-vigged
    /// market probability exactly, so edge is always ≤ 0 by construction — these
    /// factors are what let the model diverge from the market at all for these sports.
    ///
    /// Values below are placeholders (approximate historical home-win-rate vs
    /// market-implied gap per sport) — live-editable here specifically so they can
    /// be corrected without a code deploy as real settled-bet data accumulates.
    /// Cross-check against GET /Betting/stats/calibration before trusting them.
    /// </summary>
    public Dictionary<SportType, HomeAwayCalibration> HomeCalibration { get; set; } = new()
    {
        [SportType.AFL]     = new HomeAwayCalibration { Home = 1.08, Away = 0.93 },
        [SportType.NRL]     = new HomeAwayCalibration { Home = 1.06, Away = 0.95 },
        [SportType.NBA]     = new HomeAwayCalibration { Home = 1.09, Away = 0.92 },
        [SportType.Esports] = new HomeAwayCalibration { Home = 1.04, Away = 0.97 },
        // MLB's structural home-field advantage is smaller than NBA/NFL-style sports
        // (~54% raw home win rate historically) — this factor is a rough placeholder
        // in the same spirit as the others, not derived from real data yet.
        [SportType.MLB]     = new HomeAwayCalibration { Home = 1.05, Away = 0.96 },
    };
}

/// <summary>Per-sport multiplier applied to de-vigged home/away market probability.</summary>
public class HomeAwayCalibration
{
    public double Home { get; set; } = 1.0;
    public double Away { get; set; } = 1.0;
}
