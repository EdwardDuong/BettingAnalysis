import React, { useState, useEffect } from 'react';
import { getParlays } from '../services/api.js';

const SPORT_EMOJI = {
  EPL: '⚽', LaLiga: '⚽', Bundesliga: '⚽', SerieA: '⚽', Ligue1: '⚽',
  Eredivisie: '⚽', PrimeiraLiga: '⚽', MLS: '⚽', ChampionsLeague: '⚽',
  AFL: '🏈', NRL: '🏉', NBA: '🏀', MLB: '⚾', Esports: '🎮',
};

const RISK_STYLE = {
  Safe:       { bg: 'bg-green-900 border-green-700',   text: 'text-green-300',  badge: 'bg-green-700'  },
  Medium:     { bg: 'bg-yellow-900 border-yellow-700', text: 'text-yellow-300', badge: 'bg-yellow-700' },
  Aggressive: { bg: 'bg-orange-900 border-orange-700', text: 'text-orange-300', badge: 'bg-orange-700' },
  Extreme:    { bg: 'bg-red-900 border-red-700',       text: 'text-red-300',    badge: 'bg-red-700'    },
};

const STRATEGY_NOTE = {
  Safe:       'Best AI-scored picks · highest confidence',
  Medium:     'Highest-edge picks · value-focused',
  Aggressive: 'Highest-probability picks · most likely to win',
  Extreme:    'Broadest coverage · low-variance legs',
};

export default function ParlayPanel() {
  const [combos,  setCombos]  = useState([]);
  const [loading, setLoading] = useState(true);
  const [error,   setError]   = useState(null);
  const [expanded, setExpanded] = useState({});

  useEffect(() => {
    getParlays()
      .then(setCombos)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  const toggle = (legs) => setExpanded(e => ({ ...e, [legs]: !e[legs] }));

  if (loading) return <div className="animate-pulse bg-gray-800 rounded-xl h-40" />;

  if (error) return (
    <div className="bg-red-900 border border-red-600 text-red-200 rounded-xl px-5 py-4 text-sm">
      Failed to load parlays: {error}
    </div>
  );

  if (combos.length === 0) return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-8 text-center text-gray-500 text-sm">
      No parlay combos available — need at least 2 selections with edge ≥ 2% from different matches.
    </div>
  );

  return (
    <div className="space-y-4">
      <div className="bg-blue-950 border border-blue-800 rounded-xl px-4 py-3 text-xs text-blue-300 flex items-start gap-2">
        <span className="mt-0.5">📋</span>
        <div>
          <span className="font-semibold text-blue-200">Analysis only — model-generated recommendations.</span>
          {' '}These combos are built automatically from current GOOD_BET / RISKY legs.
          To track a parlay, record each leg individually using <span className="font-semibold">Record Bet</span> on the Opportunities tab.
          Drifting lines and SKIP bets are always excluded. GOOD_BET legs are prioritised.
        </div>
      </div>

      {combos.map(combo => {
        const style = RISK_STYLE[combo.riskLabel] ?? RISK_STYLE.Medium;
        const isOpen = expanded[combo.legs];

        return (
          <div key={combo.legs} className={`border rounded-xl overflow-hidden ${style.bg}`}>
            {/* Header row */}
            <div
              className="flex items-center justify-between px-5 py-4 cursor-pointer"
              onClick={() => toggle(combo.legs)}
            >
              <div className="flex items-center gap-3">
                <span className={`px-2 py-0.5 rounded text-xs font-bold text-white ${style.badge}`}>
                  {combo.legs}-LEG
                </span>
                <span className={`text-sm font-semibold ${style.text}`}>{combo.riskLabel}</span>
                <span className="text-gray-400 text-xs hidden sm:inline">
                  {STRATEGY_NOTE[combo.riskLabel] ?? ''}
                </span>
                <span className="text-gray-500 text-xs">
                  AI: {combo.avgAiScore}/10 · Edge: {(combo.avgEdge * 100).toFixed(1)}%
                </span>
              </div>

              <div className="flex items-center gap-6 text-right">
                <div>
                  <p className="text-gray-400 text-xs">Combined odds</p>
                  <p className="text-yellow-300 font-bold font-mono">{combo.combinedOdds?.toFixed(2)}</p>
                </div>
                <div>
                  <p className="text-gray-400 text-xs">Combined prob</p>
                  <p className="text-white font-mono">{((combo.combinedProb ?? 0) * 100).toFixed(1)}%</p>
                </div>
                <div>
                  <p className="text-gray-400 text-xs">EV</p>
                  <p className={`font-bold font-mono ${combo.expectedValue >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                    {combo.expectedValue >= 0 ? '+' : ''}{((combo.expectedValue ?? 0) * 100).toFixed(1)}%
                  </p>
                </div>
                <div>
                  <p className="text-gray-400 text-xs">Stake</p>
                  <p className="text-green-400 font-bold font-mono">${combo.suggestedStake?.toFixed(2)}</p>
                </div>
                <span className="text-gray-500 text-xs">{isOpen ? '▲' : '▼'}</span>
              </div>
            </div>

            {/* Leg detail */}
            {isOpen && (
              <div className="border-t border-gray-700 divide-y divide-gray-700">
                {(combo.selections ?? []).map((leg, i) => (
                  <div key={leg.matchId + leg.outcome} className="flex items-center gap-4 px-5 py-3 bg-gray-900">
                    <span className="text-gray-500 text-xs w-5">{i + 1}.</span>
                    <span className="text-lg">{SPORT_EMOJI[leg.sportType] ?? '🏆'}</span>
                    <div className="flex-1 min-w-0">
                      <p className="text-white text-sm font-medium truncate">
                        {leg.homeTeam} <span className="text-gray-500">vs</span> {leg.awayTeam}
                      </p>
                      <p className="text-blue-300 text-xs">
                        {leg.team} ({leg.outcome}) · {new Date(leg.kickoffTime).toLocaleString()}
                      </p>
                    </div>
                    <div>
                      <span className={`px-1.5 py-0.5 rounded text-xs font-semibold ${
                        leg.aiDecision === 'GOOD_BET' ? 'bg-green-800 text-green-200'
                        : leg.aiDecision === 'RISKY'  ? 'bg-yellow-800 text-yellow-200'
                        : 'bg-gray-700 text-gray-400'
                      }`}>
                        {leg.aiDecision}
                      </span>
                    </div>
                    <div className="text-right">
                      <p className="text-yellow-300 font-mono font-bold">{leg.odds?.toFixed(2)}</p>
                      <p className="text-gray-400 text-xs">{((leg.probability ?? 0) * 100).toFixed(1)}%</p>
                    </div>
                    <div className="text-right w-16">
                      <p className={`text-xs font-medium ${
                        leg.lineMovement === 'Steaming' ? 'text-green-400'
                        : leg.lineMovement === 'Drifting' ? 'text-red-400'
                        : 'text-gray-500'}`}>
                        {leg.lineMovement === 'Steaming' ? '↓ Steam'
                        : leg.lineMovement === 'Drifting' ? '↑ Drift'
                        : '→ Stable'}
                      </p>
                    </div>
                    <div className="text-right w-12">
                      <p className="text-xs text-gray-400">{((leg.edge ?? 0) * 100).toFixed(1)}%</p>
                      <p className="text-xs text-gray-500">edge</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
