using BettingAnalysis.Interfaces;
using BettingAnalysis.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BettingAnalysis.Services;

/// <summary>
/// Fetches real pre-match odds from The Odds API (https://the-odds-api.com).
/// Free tier: 500 requests/month. Each call to GetRealOdds() costs 1 request per sport.
///
/// Sport key mapping:
///   EPL        → soccer_epl
///   LaLiga     → soccer_spain_la_liga
///   Bundesliga → soccer_germany_bundesliga
///   SerieA     → soccer_italy_serie_a
///   Ligue1     → soccer_france_ligue_one
///   AFL        → aussierules_afl
///   NRL        → rugbyleague_nrl
///   NBA        → basketball_nba
///
/// European football is off-season June–July; leagues resume ~August.
/// </summary>
public class TheOddsApiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IBettingConfigService _cfg;
    private readonly ILogger<TheOddsApiService> _logger;

    // Maps our SportType enum to The Odds API sport keys
    private static readonly Dictionary<SportType, string> SportKeys = new()
    {
        { SportType.EPL,        "soccer_epl"                   },
        { SportType.LaLiga,     "soccer_spain_la_liga"         },
        { SportType.Bundesliga, "soccer_germany_bundesliga"    },
        { SportType.SerieA,     "soccer_italy_serie_a"         },
        { SportType.Ligue1,     "soccer_france_ligue_one"      },
        { SportType.AFL,        "aussierules_afl"              },
        { SportType.NRL,        "rugbyleague_nrl"              },
        { SportType.NBA,        "basketball_nba"               },
    };

    // Soccer leagues that use the Poisson goal model (as opposed to score-based calibration)
    private static readonly HashSet<SportType> SoccerLeagues = new()
        { SportType.EPL, SportType.LaLiga, SportType.Bundesliga, SportType.SerieA, SportType.Ligue1 };

    // League-specific average goals per match (home, away) and historical home win rates.
    // Used to scale expected goals by this match's market-implied win probability
    // relative to the league mean — a simplification, not a real Dixon–Coles
    // attack/defence model (no per-team ratings; every match in a league is scaled
    // by the same two constants). Because the scaling input (fairHome/fairAway) is
    // itself derived from this match's own market odds, the resulting model
    // probability stays highly correlated with the market — most of any resulting
    // "edge" reflects the Poisson round-trip of that scaling, not independent
    // forecasting skill. Treat soccer edge with the same skepticism as
    // GET /Betting/stats/calibration would justify, not as a proven signal.
    private static readonly Dictionary<SportType, (double AvgHome, double AvgAway, double AvgHomeWinRate, double AvgAwayWinRate)> SoccerParams = new()
    {
        { SportType.EPL,        (1.45, 1.05, 0.46, 0.29) },
        { SportType.LaLiga,     (1.55, 1.10, 0.50, 0.25) },  // High home advantage historically
        { SportType.Bundesliga, (1.60, 1.30, 0.44, 0.30) },  // Higher scoring, fewer draws
        { SportType.SerieA,     (1.40, 1.00, 0.44, 0.29) },  // More defensive
        { SportType.Ligue1,     (1.45, 1.05, 0.44, 0.29) },  // Similar to EPL
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TheOddsApiService(HttpClient http, IConfiguration config, IBettingConfigService cfg, ILogger<TheOddsApiService> logger)
    {
        _http    = http;
        _apiKey  = config.GetValue<string>("BettingSettings:OddsApiKey") ?? string.Empty;
        _cfg     = cfg;
        _logger  = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public static bool IsSoccerLeague(SportType sport) => SoccerLeagues.Contains(sport);

    /// <summary>
    /// Fetch live pre-match odds for all configured sports.
    /// Returns null if the API key is not set or the request fails.
    /// </summary>
    public async Task<List<MatchOdds>?> GetRealOddsAsync()
    {
        if (!IsConfigured) return null;

        var allMatches = new List<MatchOdds>();

        foreach (var (sport, sportKey) in SportKeys)
        {
            try
            {
                var matches = await FetchSportAsync(sport, sportKey);
                allMatches.AddRange(matches);
                _logger.LogInformation("Fetched {Count} matches for {Sport}", matches.Count, sport);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to fetch {Sport}: {Error}", sport, ex.Message);
            }
        }

        return allMatches;
    }

    private async Task<List<MatchOdds>> FetchSportAsync(SportType sport, string sportKey)
    {
        // The Odds API endpoint: h2h = head-to-head (win/draw/win) market
        // regions=au for Australian bookmakers, us for US, uk for UK
        // oddsFormat=decimal for decimal odds
        var url = $"https://api.the-odds-api.com/v4/sports/{sportKey}/odds/" +
                  $"?apiKey={_apiKey}&regions=au,uk,us&markets=h2h&oddsFormat=decimal&dateFormat=iso";

        var response = await _http.GetAsync(url);

        // Log remaining quota (The Odds API returns these headers)
        if (response.Headers.TryGetValues("x-requests-remaining", out var rem))
            _logger.LogInformation("Odds API requests remaining: {Remaining}", rem.First());

        response.EnsureSuccessStatusCode();

        var json    = await response.Content.ReadAsStringAsync();
        var events  = JsonSerializer.Deserialize<List<OddsApiEvent>>(json, JsonOpts)
                      ?? new List<OddsApiEvent>();

        return events
            .Select(e => MapToMatchOdds(e, sport))
            .Where(m => m != null)
            .Cast<MatchOdds>()
            .ToList();
    }

    /// <summary>
    /// Map an Odds API event to our MatchOdds model.
    /// Takes the BEST available odds across all returned bookmakers for each outcome.
    /// Lambda values are estimated from de-vigged implied probabilities, then adjusted
    /// by sport-specific home-advantage calibration (BettingConfig.HomeCalibration) so
    /// our model is independent of the market (pure de-vigged odds produce zero edge
    /// by construction — see the calibration's own doc comment for why this is a
    /// deliberately weak, unproven assumption rather than a validated forecast).
    /// </summary>
    private MatchOdds? MapToMatchOdds(OddsApiEvent ev, SportType sport)
    {
        if (ev.Bookmakers == null || ev.Bookmakers.Count == 0) return null;

        // Collect best (highest) odds per outcome across all bookmakers
        var bestOdds = new Dictionary<string, decimal>();

        foreach (var bm in ev.Bookmakers)
        {
            var h2h = bm.Markets?.FirstOrDefault(m => m.Key == "h2h");
            if (h2h?.Outcomes == null) continue;

            foreach (var outcome in h2h.Outcomes)
            {
                var price = (decimal)outcome.Price;
                if (!bestOdds.ContainsKey(outcome.Name) || price > bestOdds[outcome.Name])
                    bestOdds[outcome.Name] = price;
            }
        }

        if (!bestOdds.ContainsKey(ev.HomeTeam) || !bestOdds.ContainsKey(ev.AwayTeam))
            return null;

        decimal homeOdds = bestOdds[ev.HomeTeam];
        decimal awayOdds = bestOdds[ev.AwayTeam];
        decimal? drawOdds = bestOdds.TryGetValue("Draw", out var d) ? d : null;

        // De-vig: normalise implied probabilities to sum to 1
        double rawHome  = 1.0 / (double)homeOdds;
        double rawAway  = 1.0 / (double)awayOdds;
        double rawDraw  = drawOdds.HasValue ? 1.0 / (double)drawOdds.Value : 0;
        double total    = rawHome + rawAway + rawDraw;

        double fairHome = rawHome / total;
        double fairAway = rawAway / total;

        double homeLambda, awayLambda;

        if (SoccerLeagues.Contains(sport))
        {
            // All soccer leagues: Poisson goal model with league-specific averages.
            // Scale each team's expected goals by relative strength vs league mean.
            var p = SoccerParams.TryGetValue(sport, out var sp) ? sp : SoccerParams[SportType.EPL];
            homeLambda = p.AvgHome * (fairHome / p.AvgHomeWinRate);
            awayLambda = p.AvgAway * (fairAway / p.AvgAwayWinRate);
            homeLambda = Math.Clamp(homeLambda, 0.3, 3.5);
            awayLambda = Math.Clamp(awayLambda, 0.3, 3.5);
        }
        else
        {
            // Non-soccer: apply home-advantage calibration factor so model prob
            // differs from raw market — without this the edge is always ≤ 0.
            _cfg.Get().HomeCalibration.TryGetValue(sport, out var cal);
            double adjHome = fairHome * (cal?.Home ?? 1.0);
            double adjAway = fairAway * (cal?.Away ?? 1.0);
            // PoissonService.PredictNoDraw: P(home) = λh / (λh + λa)
            homeLambda = adjHome;
            awayLambda = adjAway;
        }

        return new MatchOdds
        {
            MatchId        = ev.Id,
            HomeTeam       = ev.HomeTeam,
            AwayTeam       = ev.AwayTeam,
            HomeOdds       = homeOdds,
            DrawOdds       = SoccerLeagues.Contains(sport) ? drawOdds : null,
            AwayOdds       = awayOdds,
            MatchStartTime = ev.CommenceTime,
            SportType      = sport,
            HomeLambda     = Math.Round(homeLambda, 3),
            AwayLambda     = Math.Round(awayLambda, 3),
        };
    }

    // ── Odds API response DTOs ────────────────────────────────────────────────

    private class OddsApiEvent
    {
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("home_team")]
        public string HomeTeam { get; set; } = string.Empty;

        [JsonPropertyName("away_team")]
        public string AwayTeam { get; set; } = string.Empty;

        [JsonPropertyName("commence_time")]
        public DateTime CommenceTime { get; set; }

        public List<Bookmaker>? Bookmakers { get; set; }
    }

    private class Bookmaker
    {
        public string Key { get; set; } = string.Empty;
        public List<Market>? Markets { get; set; }
    }

    private class Market
    {
        public string Key { get; set; } = string.Empty;
        public List<Outcome>? Outcomes { get; set; }
    }

    private class Outcome
    {
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
    }
}
