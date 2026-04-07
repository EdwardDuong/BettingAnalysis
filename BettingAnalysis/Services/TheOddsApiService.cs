using BettingAnalysis.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BettingAnalysis.Services;

/// <summary>
/// Fetches real pre-match odds from The Odds API (https://the-odds-api.com).
/// Free tier: 500 requests/month. Each call to GetRealOdds() costs 1 request per sport.
///
/// Sign up at https://the-odds-api.com to get a free API key.
/// Set it in appsettings.json under BettingSettings:OddsApiKey.
///
/// Sport key mapping:
///   EPL      → soccer_epl
///   AFL      → aussie_rules_afl
///   NRL      → rugbyleague_nrl
///   NBA      → basketball_nba
///   Esports  → esports_csgo (CS2 — most liquid esports market)
/// </summary>
public class TheOddsApiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<TheOddsApiService> _logger;

    // Maps our SportType enum to The Odds API sport keys
    private static readonly Dictionary<SportType, string> SportKeys = new()
    {
        { SportType.EPL,     "soccer_epl"         },
        { SportType.AFL,     "aussie_rules_afl"   },
        { SportType.NRL,     "rugbyleague_nrl"    },
        { SportType.NBA,     "basketball_nba"     },
        { SportType.Esports, "esports_csgo"       },  // CS2 / CSGO
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TheOddsApiService(HttpClient http, IConfiguration config, ILogger<TheOddsApiService> logger)
    {
        _http    = http;
        _apiKey  = config.GetValue<string>("BettingSettings:OddsApiKey") ?? string.Empty;
        _logger  = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

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
    /// Lambda values are estimated from de-vigged implied probabilities.
    /// </summary>
    private static MatchOdds? MapToMatchOdds(OddsApiEvent ev, SportType sport)
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

        // Estimate Poisson lambdas from fair win probabilities
        // EPL average: ~1.45 home goals, ~1.05 away goals per match
        // Scale by team strength (fair probability relative to league average)
        const double avgHomeGoals = 1.45;
        const double avgAwayGoals = 1.05;
        const double leagueAvgHomeWin = 0.46; // Historical EPL home win rate
        const double leagueAvgAwayWin = 0.29;

        double homeLambda, awayLambda;

        if (sport == SportType.EPL)
        {
            // Scale average goals by relative strength vs league mean
            homeLambda = avgHomeGoals * (fairHome / leagueAvgHomeWin);
            awayLambda = avgAwayGoals * (fairAway / leagueAvgAwayWin);
        }
        else
        {
            // For binary-outcome sports: use fair probability ratio as lambda proxy
            // The Poisson model will use these to compute P(home) = λh / (λh + λa)
            homeLambda = fairHome * 2.0;
            awayLambda = fairAway * 2.0;
        }

        // Clamp lambdas to a reasonable range (0.3 – 3.5)
        homeLambda = Math.Clamp(homeLambda, 0.3, 3.5);
        awayLambda = Math.Clamp(awayLambda, 0.3, 3.5);

        return new MatchOdds
        {
            MatchId        = ev.Id,
            HomeTeam       = ev.HomeTeam,
            AwayTeam       = ev.AwayTeam,
            HomeOdds       = homeOdds,
            DrawOdds       = sport == SportType.EPL ? drawOdds : null,
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
