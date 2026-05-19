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
| GET    | `/Betting/opportunities`    | Pre-match value bets (edge ≥ 5%, Rule #1 & #2)    |
| POST   | `/Betting/place`            | Place a bet with full validation gate              |
| GET    | `/Betting/history`          | All placed bets (audit log)                        |
| GET    | `/Betting/bankroll`         | Current bankroll state and limit flags             |
| POST   | `/Betting/bankroll/reset`   | Reset bankroll counters (new session)              |
| POST   | `/Betting/result/{id}`      | Record Win/Loss and update bankroll                |
| GET    | `/Betting/stats`            | Aggregate win rate, PnL, ROI, and current streak   |
| GET    | `/Betting/stats/sport`      | Win/loss/PnL breakdown per sport                   |
| GET    | `/Betting/parlays`          | Suggested 2–4 leg parlay combos from GOOD_BETs     |
| GET    | `/Betting/prediction/{id}`  | Poisson model detail for a single match            |
| GET    | `/Betting/export/csv`       | Download full bet history as CSV                   |
| GET    | `/Betting/settings`         | Current live config                                |
| PUT    | `/Betting/settings`         | Update config (applies immediately, no restart)    |
| POST   | `/Betting/refresh`          | Invalidate odds cache                              |
| GET    | `/Betting/rejected`         | Bets blocked by validation gate                    |
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

| Key                       | Default   | Rule | Description                                      |
|---------------------------|-----------|------|--------------------------------------------------|
| `InitialBankroll`         | 10000     | —    | Starting bankroll in dollars                     |
| `KellyFraction`           | 0.5       | —    | Fractional Kelly (0.5 = half-Kelly)              |
| `MaxStakePercent`         | 0.03      | #3   | Max stake per bet as fraction of bankroll        |
| `DailyLossLimitPercent`   | 0.10      | #4   | Stop betting today if daily loss exceeds this    |
| `StopLossPercent`         | 0.20      | #5   | Halt system if cumulative loss exceeds this      |
| `EdgeThreshold`           | 0.05      | #2   | Minimum model edge to show an opportunity        |
| `PreMatchMinHoursAhead`   | 1.0       | #1   | Only consider matches ≥ this many hours away     |
| `PreMatchMaxHoursAhead`   | 336.0     | #1   | Do not bet more than this many hours ahead (336h = 2 weeks) |

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

| #  | Rule                    | Enforcement                                                  |
|----|-------------------------|--------------------------------------------------------------|
| 1  | Pre-match only          | OddsService filters `MatchStartTime` between now+1h and now+2wk |
| 2  | Edge ≥ 5%               | BettingController skips any outcome below EdgeThreshold       |
| 3  | Max stake 2–5%          | BetSizingService caps Kelly at MaxStakePercent               |
| 4  | Daily loss ≤ 10%        | BankrollService rejects bets once DailyLossUsed hits limit   |
| 5  | Stop-loss at 20%        | System halts; opportunities endpoint returns empty list      |
| 6  | Market focus            | Lambdas calibrated for mid-tier matches; EPL mains avoided   |
| 7  | 30–60 min refresh       | Auto-refresh every 5 min in demo; configurable in production |
| 8  | Verify edge > 20%       | `RequiresVerification` flag shown in UI and API response     |
| 9  | Full logging            | Every bet logged with prediction, edge, stake, result        |
| 10 | Bankroll sync           | Bankroll updated immediately after every Win/Loss result     |

---

## Probability Model

**EPL (soccer):**
Full Poisson matrix. Each team's goals are independent Poisson(λ) variables.
Score combinations 0–10 × 0–10 are enumerated and classified as home/draw/away win.
Probabilities are renormalized to correct for truncation.

**AFL / NRL / NBA / Esports:**
Binary outcome model (no draw).
`P(Home win) = λ_home / (λ_home + λ_away)`
Home advantage is baked into the higher home lambda.

**Calibration (production):**
- EPL: Dixon–Coles attack/defence ratings from last 38 games.
- AFL/NRL: Points differential normalized per league average.
- NBA: Offensive rating vs defensive rating ratio.
- Esports: Rolling 90-day win rate per map/format.

---

## Extending to Real Odds

Replace `OddsService.GetMockOdds()` with:

```csharp
// Example: The Odds API
var client = new HttpClient();
var json = await client.GetStringAsync(
    "https://api.the-odds-api.com/v4/sports/soccer_epl/odds/?apiKey=KEY&markets=h2h");
// Deserialize and map to List<MatchOdds>
```

Then calibrate `HomeLambda` / `AwayLambda` from a separate stats API
(e.g. Football-Data.org, SportMonks).

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Frontend shows "API error" | Backend not running | Run `dotnet run` in `BettingAnalysis/` |
| All opportunities list empty | Stop-loss triggered, or no matches in 1h–2wk window | Check `/Betting/bankroll` for `isStopLossTriggered`; use mock data outside AU hours |
| Odds never refresh | Cache still valid (30 min TTL) | Click "⟳ New Odds" or call `POST /Betting/refresh` |
| Bet rejected with exposure error | Too many open (Pending) bets | Settle pending bets via History tab before placing new ones |
| Parlay tab shows nothing | Fewer than 2 GOOD_BET opportunities | Lower `edgeThreshold` in Settings or wait for better odds |
| Contribution graph not updating | Commit email mismatch | Ensure Git email matches a verified GitHub account email |

---

## Production Checklist

- [ ] Replace in-memory stores with EF Core + SQL Server/PostgreSQL
- [ ] Add JWT authentication to all endpoints
- [ ] Add rate limiting (prevent rapid-fire bet placement)
- [ ] Schedule odds refresh via a background `IHostedService`
- [ ] Add unit tests for PoissonService, EdgeService, BetSizingService
- [ ] Add integration tests for BettingController
- [ ] Move bankroll state to Redis for distributed deployments
- [ ] Set up Seq or Application Insights for structured logging
