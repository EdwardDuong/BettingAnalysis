using BettingAnalysis.Models;

namespace BettingAnalysis.Services;

/// <summary>
/// Odds service with timing-window enforcement and line movement data.
/// Now uses BettingConfigService for the 1–6 hour betting window (live-updateable).
/// Previous odds are included to enable LineMovementService analysis.
/// </summary>
public class OddsService
{
    private readonly BettingConfigService _cfg;
    private readonly TheOddsApiService    _realApi;
    private readonly ILogger<OddsService> _logger;

    private List<MatchOdds>? _cache;
    private DateTime         _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public OddsService(BettingConfigService cfg, TheOddsApiService realApi, ILogger<OddsService> logger)
    {
        _cfg     = cfg;
        _realApi = realApi;
        _logger  = logger;
    }

    /// <summary>
    /// Returns matches within the configured betting window.
    /// Window: kickoff between now+minHours and now+maxHours (default 1–6h).
    /// </summary>
    public List<MatchOdds> GetPreMatchOdds()
    {
        var config = _cfg.Get();
        var all    = GetAllOdds();
        var now    = DateTime.UtcNow;
        var minKickoff = now.AddHours(config.PreMatchMinHours);
        var maxKickoff = now.AddHours(config.PreMatchMaxHours);

        return all
            .Where(m => m.MatchStartTime > minKickoff && m.MatchStartTime <= maxKickoff)
            .OrderBy(m => m.MatchStartTime)
            .ToList();
    }

    public void InvalidateCache() => _cache = null;

    // ── Data source: real API or mock ─────────────────────────────────────────

    private List<MatchOdds> GetAllOdds()
    {
        if (_cache != null && DateTime.UtcNow < _cacheExpiry) return _cache;

        if (_realApi.IsConfigured)
        {
            try
            {
                _cacheLock.Wait();
                try
                {
                    if (_cache != null && DateTime.UtcNow < _cacheExpiry) return _cache;
                    var real = _realApi.GetRealOddsAsync().GetAwaiter().GetResult();
                    if (real?.Count > 0)
                    {
                        _cache = real; _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
                        _logger.LogInformation("Loaded {Count} real matches. Cache until {T:HH:mm}", real.Count, _cacheExpiry);
                        return _cache;
                    }
                }
                finally { _cacheLock.Release(); }
            }
            catch (Exception ex) { _logger.LogWarning("Real API failed, using mock: {E}", ex.Message); }
        }

        _cache = GetMockOdds();
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        return _cache;
    }

    // ── Mock data — all matches within 1–6h window + previous odds ────────────

    private static List<MatchOdds> GetMockOdds()
    {
        var now = DateTime.UtcNow;
        return
        [
            // EPL — steaming home (odds shortening = favorable)
            new() {
                MatchId="EPL-001", HomeTeam="Arsenal",         AwayTeam="Chelsea",
                HomeOdds=2.10m,  DrawOdds=3.40m,  AwayOdds=3.80m,
                PreviousHomeOdds=2.25m, PreviousDrawOdds=3.40m, PreviousAwayOdds=3.60m,  // Home steaming ✅
                MatchStartTime=now.AddHours(2.5), SportType=SportType.EPL, HomeLambda=1.85, AwayLambda=0.95
            },
            // EPL — away drifting (line moving against away = block)
            new() {
                MatchId="EPL-002", HomeTeam="Manchester City",  AwayTeam="Liverpool",
                HomeOdds=1.95m,  DrawOdds=3.60m,  AwayOdds=4.10m,
                PreviousHomeOdds=1.98m, PreviousDrawOdds=3.55m, PreviousAwayOdds=3.80m,  // Away drifting ❌
                MatchStartTime=now.AddHours(4), SportType=SportType.EPL, HomeLambda=2.20, AwayLambda=1.30
            },
            // AFL — stable
            new() {
                MatchId="AFL-001", HomeTeam="Collingwood",      AwayTeam="Richmond",
                HomeOdds=1.75m, DrawOdds=null, AwayOdds=2.20m,
                PreviousHomeOdds=1.78m, PreviousAwayOdds=2.18m,                           // Stable
                MatchStartTime=now.AddHours(3), SportType=SportType.AFL, HomeLambda=1.65, AwayLambda=1.05
            },
            // AFL — home steaming
            new() {
                MatchId="AFL-002", HomeTeam="Geelong",          AwayTeam="Hawthorn",
                HomeOdds=1.55m, DrawOdds=null, AwayOdds=2.60m,
                PreviousHomeOdds=1.70m, PreviousAwayOdds=2.40m,                           // Home steaming ✅
                MatchStartTime=now.AddHours(5.5), SportType=SportType.AFL, HomeLambda=1.90, AwayLambda=0.90
            },
            // NRL — stable
            new() {
                MatchId="NRL-001", HomeTeam="Penrith Panthers", AwayTeam="Melbourne Storm",
                HomeOdds=1.85m, DrawOdds=null, AwayOdds=2.05m,
                PreviousHomeOdds=1.83m, PreviousAwayOdds=2.08m,
                MatchStartTime=now.AddHours(1.5), SportType=SportType.NRL, HomeLambda=1.55, AwayLambda=1.40
            },
            // NRL — away steaming
            new() {
                MatchId="NRL-002", HomeTeam="Brisbane Broncos", AwayTeam="Cronulla Sharks",
                HomeOdds=2.40m, DrawOdds=null, AwayOdds=1.65m,
                PreviousHomeOdds=2.30m, PreviousAwayOdds=1.80m,                           // Away steaming ✅
                MatchStartTime=now.AddHours(3.5), SportType=SportType.NRL, HomeLambda=1.10, AwayLambda=1.75
            },
            // NBA — stable
            new() {
                MatchId="NBA-001", HomeTeam="Boston Celtics",   AwayTeam="Miami Heat",
                HomeOdds=1.60m, DrawOdds=null, AwayOdds=2.50m,
                PreviousHomeOdds=1.62m, PreviousAwayOdds=2.48m,
                MatchStartTime=now.AddHours(2), SportType=SportType.NBA, HomeLambda=1.80, AwayLambda=1.05
            },
            // NBA — home drifting (warning)
            new() {
                MatchId="NBA-002", HomeTeam="LA Lakers",        AwayTeam="Golden State Warriors",
                HomeOdds=2.10m, DrawOdds=null, AwayOdds=1.80m,
                PreviousHomeOdds=1.95m, PreviousAwayOdds=1.90m,                           // Home drifting ❌
                MatchStartTime=now.AddHours(4.5), SportType=SportType.NBA, HomeLambda=1.30, AwayLambda=1.55
            },
            // Esports — steaming
            new() {
                MatchId="ESP-001", HomeTeam="Team Liquid",      AwayTeam="Natus Vincere",
                HomeOdds=2.05m, DrawOdds=null, AwayOdds=1.85m,
                PreviousHomeOdds=2.20m, PreviousAwayOdds=1.75m,
                MatchStartTime=now.AddHours(1.5), SportType=SportType.Esports, HomeLambda=1.30, AwayLambda=1.45
            },
            // Esports — stable
            new() {
                MatchId="ESP-002", HomeTeam="FaZe Clan",        AwayTeam="G2 Esports",
                HomeOdds=1.70m, DrawOdds=null, AwayOdds=2.25m,
                PreviousHomeOdds=1.72m, PreviousAwayOdds=2.22m,
                MatchStartTime=now.AddHours(5), SportType=SportType.Esports, HomeLambda=1.75, AwayLambda=1.10
            },
            // Match outside 6h window — excluded by timing rule
            new() {
                MatchId="EPL-FAR", HomeTeam="Brighton",         AwayTeam="Brentford",
                HomeOdds=2.30m, DrawOdds=3.20m, AwayOdds=3.10m,
                MatchStartTime=now.AddHours(24), SportType=SportType.EPL, HomeLambda=1.50, AwayLambda=1.45
            },
            // Match < 1h away — excluded by timing rule
            new() {
                MatchId="EPL-CLOSE", HomeTeam="West Ham",       AwayTeam="Everton",
                HomeOdds=2.00m, DrawOdds=3.30m, AwayOdds=3.90m,
                MatchStartTime=now.AddMinutes(30), SportType=SportType.EPL, HomeLambda=1.40, AwayLambda=1.20
            },
        ];
    }
}
