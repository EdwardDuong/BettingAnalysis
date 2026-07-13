# Betting Analysis — Pre-Match Edge Finder

Local, multi-sport pre-match betting system with Poisson probability model,
Kelly criterion stake sizing, and full risk management enforcement.

---

## Architecture

```
BettingAnalysis/
├── BettingAnalysis/          ← .NET 8 Web API (backend)
│   ├── Controllers/
│   │   └── BettingController.cs
│   ├── Models/
│   │   ├── MatchOdds.cs
│   │   ├── PredictionResult.cs
│   │   ├── BetOpportunity.cs
│   │   ├── BetHistory.cs
│   │   ├── Bankroll.cs
│   │   └── PlaceBetRequest.cs
│   ├── Services/
│   │   ├── PoissonService.cs        — Outcome probabilities
│   │   ├── EdgeService.cs           — Model edge vs implied prob
│   │   ├── OddsService.cs           — Real + mock pre-match odds
│   │   ├── TheOddsApiService.cs     — The Odds API integration
│   │   ├── BetSizingService.cs      — Half-Kelly criterion
│   │   ├── BankrollService.cs       — Bankroll state and limits
│   │   ├── BettingLoggingService.cs — Audit trail and stats
│   │   ├── ValidationService.cs     — 11-rule validation gate
│   │   ├── AIValidatorService.cs    — Scoring and GOOD/RISKY/SKIP
│   │   ├── LineMovementService.cs   — Steam/drift detection
│   │   ├── CLVService.cs            — Closing line value
│   │   ├── ParlayService.cs         — Multi-leg combo builder
│   │   └── BettingConfigService.cs  — Live-editable config
│   ├── Program.cs
│   └── appsettings.json
│
└── Frontend/                 ← React + Tailwind (frontend)
    ├── src/
    │   ├── components/
    │   │   ├── BankrollPanel.jsx       — health score + utilisation bars
    │   │   ├── OpportunitiesTable.jsx  — sort, search, min-odds filter, confidence badge
    │   │   ├── BetHistoryTable.jsx     — CLV row colouring, CSV export
    │   │   ├── AnalyticsPanel.jsx      — ROI cards, P&L chart, edge distribution
    │   │   ├── ParlayPanel.jsx         — multi-leg combo builder
    │   │   ├── RejectedBetsPanel.jsx   — validation gate rejection log
    │   │   └── SettingsPanel.jsx
    │   ├── services/api.js
    │   ├── App.jsx
    │   └── main.jsx
    ├── package.json
    ├── vite.config.js
    └── tailwind.config.js
```

---

## Prerequisites

| Tool           | Version  |
|----------------|----------|
| .NET SDK       | 8.0+     |
| Node.js        | 18+      |
| npm            | 9+       |

---

## Running Locally

### 1 — Backend (.NET API)

```bash
cd BettingAnalysis          # the inner project folder with .csproj

dotnet run
# API listens on http://localhost:5000
# Swagger UI: http://localhost:5000/swagger
```

### 2 — Frontend (React + Vite)

```bash
cd Frontend

npm install
npm run dev
# Opens http://localhost:5173
```

> The Vite dev server proxies `/Betting/*` to `http://localhost:5000`, so no CORS
> issues during development. Both processes must run simultaneously.

---

## API Endpoints

| Method | Path                        | Description                                        |
|--------|-----------------------------|----------------------------------------------------|
| GET    | `/Betting/opportunities`    | Pre-match value bets, all labelled GOOD_BET/RISKY/SKIP (Rule #1 & #2) — SKIP items are still returned, not filtered out |
| POST   | `/Betting/place`            | Place a bet with full validation gate              |
| GET    | `/Betting/history`          | All placed bets (audit log)                        |
| GET    | `/Betting/bankroll`         | Current bankroll state and limit flags             |
| POST   | `/Betting/bankroll/reset`   | Reset bankroll counters (new session)              |
| POST   | `/Betting/result/{id}`      | Record Win/Loss and update bankroll                |
| GET    | `/Betting/stats`            | Aggregate win rate, PnL, ROI, and current streak   |
| GET    | `/Betting/stats/sport`      | Win/loss/PnL breakdown per sport                   |
| GET    | `/Betting/parlays`          | Suggested 3–5 leg parlay combos from GOOD_BETs     |
| GET    | `/Betting/daily-double`     | Safest single leg or small parlay clearing 2x combined odds |
| GET    | `/Betting/prediction/{id}`  | Poisson model detail for a single match            |
| GET    | `/Betting/export/csv`       | Download full bet history as CSV                   |
| GET    | `/Betting/settings`         | Current live config                                |
| PUT    | `/Betting/settings`         | Update config (applies immediately, no restart)    |
| POST   | `/Betting/refresh`          | Invalidate odds cache                              |
| GET    | `/Betting/rejected`         | Bets blocked by validation gate                    |
| GET    | `/Betting/stats/calibration`| Predicted-probability vs actual-win-rate buckets, to check model calibration against real results |
| GET    | `/health`                   | Health check                                       |

### Example: Place a bet

```json
POST /Betting/place
{
  "matchId": "EPL-001",
  "outcome": "Home",
  "customStake": null
}
```

`customStake: null` uses the Kelly-calculated suggested stake.

### Example: Record result

```json
POST /Betting/result/{guid}
"Win"
```

---

## Configuration (`appsettings.json`)

Values below are the actual current `appsettings.json` values, not the C# fallback
defaults baked into `BettingConfigService` — the two are not always the same
(`EdgeThreshold` and `MaxConsecutiveLosses` currently differ between them; see the
Risk Management Rules section).

| Key                       | Current value | Rule | Description                                      |
|---------------------------|-----------|------|--------------------------------------------------|
| `InitialBankroll`         | **500**   | —    | Starting bankroll in dollars                     |
| `KellyFraction`           | 0.5       | —    | Fractional Kelly (0.5 = half-Kelly)              |
| `MaxStakePercent`         | **0.10**  | #3   | Max stake per bet as fraction of bankroll (fixed 10% — the maximum `PUT /Betting/settings` allows; $50 on the current $500 bankroll) |
| `DailyLossLimitPercent`   | 0.10      | #4   | Stop betting today once losses exceed this fraction of the bankroll at the start of the day |
| `StopLossPercent`         | 0.20      | #5   | Halt system if cumulative loss exceeds this fraction of the *initial* bankroll |
| `MaxExposurePercent`      | 0.10      | #8   | Total pending-bet stake must not exceed this fraction of current bankroll |
| `EdgeThreshold`           | **0.04**  | #2   | Minimum model edge to allow *placing* a bet — below this, `/opportunities` still lists the pick (labelled SKIP) but `/place` rejects it |
| `HighEdgeThreshold`       | 0.20      | #8   | Edge above this triggers a manual-verification warning, not a block |
| `ParlayMinEdge`           | 0.02      | —    | Minimum edge for a leg to be eligible for a parlay/daily-double combo |
| `PreMatchMinHoursAhead`   | 1.0       | #1   | Only consider matches ≥ this many hours away     |
| `PreMatchMaxHoursAhead`   | 336.0     | #1   | Do not bet more than this many hours ahead (336h = 2 weeks) |
| `MaxConsecutiveLosses`    | **5**     | #10  | Halt betting (tilt protection) after this many losses in a row |
| `MaxBetsPerMatch`         | 2         | #9   | Max simultaneous bets on the same match           |
| `TeamBlacklist`           | `[]`      | #11  | Teams never bet on regardless of edge — empty by default, so inactive until populated |
| `GoodBetMaxStake` / `RiskyMaxStake` | 25 / 2.5 | — | Secondary per-decision stake caps, scaled to the $500 bankroll (previously 500/50 for a $10k bankroll) — both stay tighter than `MaxStakePercent`'s $50 cap, so they do bind now |
| `Parlay3/4/5MaxStake`     | 5 / 3.75 / 2.5 | — | Per-tier parlay stake caps, scaled to the $500 bankroll (previously 100/75/50) |
| `DailyDoubleTargetOdds`   | 2.0       | —    | Target combined odds for `/Betting/daily-double`  |
| `DailyDoubleMaxLegs`      | 20        | —    | Max legs the daily-double pick will combine       |
| `DailyDoubleMaxStake`     | **5**     | —    | Dollar cap for the daily-double pick's stake, scaled to the $500 bankroll (previously 100) |
| `SoccerCalibrationShrinkage` | 0.5    | —    | Dampens soccer lambda scaling for lopsided matches — 1.0 = old undamped behaviour, 0.0 = always predict the league average (zero edge). See Probability Model above. |

---

## Opportunity Fields

Each opportunity returned by `GET /Betting/opportunities` includes:

| Field             | Type    | Description                                                        |
|-------------------|---------|--------------------------------------------------------------------|
| `edge`            | float   | Model edge over implied probability (e.g. 0.08 = 8%)              |
| `confidenceLevel` | string  | `"High"` / `"Medium"` / `"Low"` — derived from edge + model prob  |
| `aiValidation`    | object  | `decision` (GOOD_BET/RISKY/SKIP), `score` (0–10), `flags`, `reason` |
| `lineMovementStatus` | string | `"Steaming"` / `"Drifting"` / `"Stable"`                       |
| `suggestedStake`  | decimal | Half-Kelly stake, capped at `MaxStakePercent`                      |
| `hoursUntilKickoff` | float | Hours until match starts (1.0–6.0)                               |

**Confidence level thresholds:**
- `High` — edge ≥ 15% AND model probability ≥ 60%
- `Medium` — edge ≥ 10% OR model probability ≥ 58%
- `Low` — below both thresholds (still above edge minimum)

---

## Risk Management Rules

`ValidationService.ValidateAsync` enforces 11 numbered checks at bet-placement time,
not 10 — this table previously omitted #8, #9, #10 (renumbered below), #11. All
"current value" figures below are read live from `BettingConfig` (`GET
/Betting/settings`), which is seeded from `appsettings.json` at startup and can be
changed at runtime by an Admin via `PUT /Betting/settings`.

| #  | Rule                    | Enforcement                                                  |
|----|-------------------------|--------------------------------------------------------------|
| 1  | Pre-match only          | `OddsService` filters `MatchStartTime` between now+1h and now+2wk |
| 2  | Edge ≥ 4%               | `ValidationService` **rejects placement** below `EdgeThreshold` (currently 4%, not 5%). `/opportunities` does *not* filter these out — it still returns them labelled `SKIP`, with the placement button disabled client-side. |
| 3  | Max stake ≤ 10% of bankroll | `BetSizingService` caps half-Kelly at `MaxStakePercent` (a fixed 10% ceiling — the max `PUT /Betting/settings` allows — not a range); `ValidationService` re-checks the final stake against it before placement. $50 on the current $500 bankroll. |
| 4  | Daily loss ≤ 10%        | `BankrollService` rejects further bets once `DailyLossUsed` hits `DailyLossLimit` — fixed to the bankroll at the *start of the current day*, so the limit doesn't shrink further as losses accrue during the day |
| 5  | Stop-loss at 20%        | System halts once cumulative loss hits 20% of the *initial* bankroll; `/opportunities` and `/parlays` return empty lists while active |
| 6  | Market focus            | `AIValidatorService` adds a soft `BIG_MATCHUP_LOW_EDGE` penalty (score −1, not a hard block) when edge is below `BigMatchupEdgeThreshold` (8%) *and* both teams are on that league's `BigTeams` list — a hand-curated set of the most heavily-bet clubs per league (`BettingConfig.BigTeams`). Applies to all 9 soccer leagues, not just EPL; deliberately excludes MLS (markets aren't efficiently-priced the same way) and Champions League (nearly every participant would count as "big"). |
| 7  | Odds refresh interval   | Background refresh every `OddsRefreshMinutes` (default 30, not currently set in `appsettings.json`). There is no separate "5 min demo mode" — one interval applies everywhere. |
| 8  | Exposure ≤ 10% of bankroll | `ValidationService` rejects a bet if total pending stake (`TotalExposure`) plus the new stake would exceed `MaxExposurePercent` of current bankroll; warns at 80% of that limit |
| 9  | Max 2 bets per match    | `ValidationService` rejects a bet once `MaxBetsPerMatch` bets already exist on that match |
| 10 | Tilt protection         | `ValidationService` halts betting after `MaxConsecutiveLosses` losses in a row (currently 5); warns one loss before the limit |
| 11 | Team blacklist          | `ValidationService` rejects any bet on a team in `TeamBlacklist` — empty by default, so inactive until populated |
| —  | High-edge warning       | Edge ≥ `HighEdgeThreshold` (20%) doesn't block the bet — it adds a warning ("manually verify Poisson inputs") surfaced in `ValidationResult.Warnings` and the `AIValidatorService` `HIGH_EDGE` flag. `BetOpportunity.RequiresManualCheck` is computed for the same condition but is not currently read by the frontend. |
| —  | Full logging            | Every bet logged with prediction, edge, stake, result (`BettingLoggingService`) |
| —  | Bankroll sync           | Bankroll updated immediately after every Win/Loss result (`BankrollService.UpdateAfterResultAsync`) |

---

## Probability Model

**Soccer leagues** (EPL, LaLiga, Bundesliga, SerieA, Ligue1, Eredivisie, PrimeiraLiga,
MLS, ChampionsLeague — see `SportTypeExtensions.IsSoccerLeague`):
Full Poisson matrix. Each team's goals are independent Poisson(λ) variables.
Score combinations 0–10 × 0–10 are enumerated and classified as home/draw/away win.
Probabilities are renormalized to correct for truncation.

**AFL / NRL / NBA / MLB / Esports:**
Binary outcome model (no draw).
`P(Home win) = λ_home / (λ_home + λ_away)`
Home advantage is baked into the higher home lambda.

**Calibration (current implementation, not aspirational):**
Both paths derive lambda from *this match's own de-vigged market odds*, not from
independent historical team ratings:
- **Soccer**: `TheOddsApiService.SoccerParams` scales each league's average goals by
  this match's market-implied win probability relative to the league mean — a
  simplification, not real Dixon–Coles attack/defence ratings (no per-team data is
  used). Because the scaling input comes from the match's own odds, the resulting
  "edge" is largely a Poisson round-trip of the market's own price, not independent
  forecasting skill. The raw ratio-to-league-average scaling amplifies deviation for
  lopsided matches (a big favourite/underdog far from the league's assumed average
  win rate) instead of damping it, so `BettingConfig.SoccerCalibrationShrinkage`
  (default 0.5) blends it back toward 1.0 — favouring plausible, consistent
  predictions over occasionally-large-but-unreliable edge on outlier matches.
- **Non-soccer**: `BettingConfig.HomeCalibration` applies a fixed per-sport
  home-advantage multiplier to the de-vigged market probability. Without it, model
  probability equals the market's exactly and edge is always ≤ 0 by construction —
  the multiplier is what lets the model diverge from the market at all.
- Both sets of calibration constants are **unverified placeholders**, not derived
  from real historical results. Check `GET /Betting/stats/calibration` before
  trusting edge on any sport/league once enough bets have settled to judge it.

---

## Real Odds Integration

Already implemented — `TheOddsApiService` fetches live pre-match odds from
[The Odds API](https://the-odds-api.com) for every sport in `SportKeys`. Set
`BettingSettings:OddsApiKey` (via `dotnet user-secrets` in dev, or the
`BETTINGSETTINGS__ODDSAPIKEY` environment variable in production) and
`OddsService` automatically switches from mock data to real odds — no code
change needed. Free tier is 500 requests/month; each cache refresh
(`OddsRefreshMinutes`, default 30 min) costs 1 request per sport.

`HomeLambda`/`AwayLambda` are calibrated from the match's own de-vigged market
odds, not from an independent stats API — see Probability Model above for why
that makes calibration accuracy an open question, not a solved one.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Frontend shows "API error" | Backend not running | Run `dotnet run` in `BettingAnalysis/` |
| All opportunities list empty | Stop-loss triggered, or no matches in 1h–2wk window | Check `/Betting/bankroll` for `isStopLossTriggered`; if no real `OddsApiKey` is set, mock data is used and always has matches in-window |
| Odds never refresh | Cache still valid (30 min TTL) | Click "⟳ New Odds" or call `POST /Betting/refresh` |
| Bet rejected with exposure error | Too many open (Pending) bets | Settle pending bets via History tab before placing new ones |
| Parlay tab shows nothing | Fewer than 3 eligible legs for any tier | Lower `edgeThreshold`/`ParlayMinEdge` in Settings or wait for better odds |
| Daily Double shows "no safe way to clear the target" | No combination of today's eligible legs reaches `DailyDoubleTargetOdds` | Lower `DailyDoubleTargetOdds` or raise `DailyDoubleMaxLegs` in Settings |
| Contribution graph not updating | Commit email mismatch | Ensure Git email matches a verified GitHub account email |

---

## Production Checklist

- [x] Replace in-memory stores with EF Core + SQL Server
- [x] Add JWT authentication to all endpoints
- [x] Add rate limiting (prevent rapid-fire bet placement)
- [x] Schedule odds refresh via a background `IHostedService`
- [x] Add unit tests for PoissonService, EdgeService, BetSizingService
- [x] Add integration/unit tests for BettingController (`BettingControllerTests`)
- [x] Implement Rule #6 (market focus — `BettingConfig.BigTeams` + `BigMatchupEdgeThreshold`)
- [ ] Move bankroll state to Redis for distributed deployments (currently per-instance in-memory, backed by DB snapshots)
- [ ] Set up Seq or Application Insights for structured logging (currently console + rolling file via Serilog)
- [ ] Validate calibration constants (`SoccerParams`, `HomeCalibration`, `BigTeams`) against real settled-bet results via `GET /Betting/stats/calibration` — currently unverified placeholders
