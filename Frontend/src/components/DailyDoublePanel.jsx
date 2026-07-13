import React, { useState, useEffect } from 'react';
import { getDailyDouble } from '../services/api.js';

const SPORT_EMOJI = {
  EPL: '⚽', LaLiga: '⚽', Bundesliga: '⚽', SerieA: '⚽', Ligue1: '⚽',
  Eredivisie: '⚽', PrimeiraLiga: '⚽', MLS: '⚽', ChampionsLeague: '⚽',
  AFL: '🏈', NRL: '🏉', NBA: '🏀', MLB: '⚾', Esports: '🎮',
};

export default function DailyDoublePanel() {
  const [pick,     setPick]     = useState(null);
  const [message,  setMessage]  = useState(null);
  const [loading,  setLoading]  = useState(true);
  const [error,    setError]    = useState(null);
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    getDailyDouble()
      .then(data => {
        if (data?.legs) setPick(data);
        else setMessage(data?.message ?? 'No safe double available today.');
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="animate-pulse bg-gray-800 rounded-xl h-16" />;

  if (error) return (
    <div className="bg-red-900 border border-red-600 text-red-200 rounded-xl px-5 py-4 text-sm">
      Failed to load daily double: {error}
    </div>
  );

  if (!pick) return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl px-5 py-4 text-sm text-gray-400 flex items-center gap-2">
      <span>🎯</span><span>{message}</span>
    </div>
  );

  return (
    <div className="border border-purple-700 bg-purple-950 rounded-xl overflow-hidden">
      <div
        className="flex items-center justify-between px-5 py-4 cursor-pointer"
        onClick={() => setExpanded(e => !e)}
      >
        <div className="flex items-center gap-3">
          <span className="px-2 py-0.5 rounded text-xs font-bold text-white bg-purple-700">
            🎯 DAILY DOUBLE
          </span>
          <span className="text-sm font-semibold text-purple-300">
            {pick.riskLabel === 'Single' ? 'Single leg' : `${pick.legs}-leg parlay`}
          </span>
          <span className="text-gray-400 text-xs hidden md:inline max-w-md truncate">{pick.strategy}</span>
        </div>

        <div className="flex items-center gap-6 text-right">
          <div>
            <p className="text-gray-400 text-xs">Combined odds</p>
            <p className="text-yellow-300 font-bold font-mono">{pick.combinedOdds?.toFixed(2)}</p>
          </div>
          <div>
            <p className="text-gray-400 text-xs">Win prob</p>
            <p className="text-white font-mono">{((pick.combinedProb ?? 0) * 100).toFixed(1)}%</p>
          </div>
          <div>
            <p className="text-gray-400 text-xs">EV</p>
            <p className={`font-bold font-mono ${pick.expectedValue >= 0 ? 'text-green-400' : 'text-red-400'}`}>
              {pick.expectedValue >= 0 ? '+' : ''}{((pick.expectedValue ?? 0) * 100).toFixed(1)}%
            </p>
          </div>
          <div>
            <p className="text-gray-400 text-xs">Stake</p>
            <p className="text-green-400 font-bold font-mono">${pick.suggestedStake?.toFixed(2)}</p>
          </div>
          <span className="text-gray-500 text-xs">{expanded ? '▲' : '▼'}</span>
        </div>
      </div>

      {expanded && (
        <div className="border-t border-purple-800 divide-y divide-purple-900">
          {(pick.selections ?? []).map((leg, i) => (
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
              <div className="text-right">
                <p className="text-yellow-300 font-mono font-bold">{leg.odds?.toFixed(2)}</p>
                <p className="text-gray-400 text-xs">{((leg.probability ?? 0) * 100).toFixed(1)}%</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
