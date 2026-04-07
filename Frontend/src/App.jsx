import React, { useState, useEffect, useCallback } from 'react';
import BankrollPanel      from './components/BankrollPanel.jsx';
import OpportunitiesTable from './components/OpportunitiesTable.jsx';
import BetHistoryTable    from './components/BetHistoryTable.jsx';
import SettingsPanel      from './components/SettingsPanel.jsx';
import { getOpportunities, getHistory, getBankroll, refreshOdds } from './services/api.js';

const SPORTS       = ['All', 'EPL', 'AFL', 'NRL', 'NBA', 'Esports'];
const MAIN_TABS    = ['Opportunities', 'History', 'Settings'];
const SPORT_EMOJI  = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮', All: '🌐' };

/**
 * App — root component.
 *
 * Data flow:
 *   Fetches opportunities, history, and bankroll on mount.
 *   Refreshes opportunities every 5 minutes (Rule #7: 30–60 min in production).
 *   Passes refresh callbacks down so child actions (place bet, mark result)
 *   trigger a data reload.
 *
 * Tab structure:
 *   Top nav: Opportunities | History | Settings
 *   Sport filter (within Opportunities): All | EPL | AFL | NRL | NBA | Esports
 */
export default function App() {
  const [opportunities, setOpportunities] = useState([]);
  const [history,       setHistory]       = useState([]);
  const [bankroll,      setBankroll]      = useState(null);
  const [mainTab,       setMainTab]       = useState('Opportunities');
  const [sport,         setSport]         = useState('All');
  const [loading,       setLoading]       = useState(true);
  const [error,         setError]         = useState(null);
  const [lastRefresh,   setLastRefresh]   = useState(null);

  const fetchAll = useCallback(async () => {
    try {
      const [opps, hist, br] = await Promise.all([
        getOpportunities(),
        getHistory(),
        getBankroll(),
      ]);
      setOpportunities(opps);
      setHistory(hist);
      setBankroll(br);
      setLastRefresh(new Date());
      setError(null);
    } catch (err) {
      setError(`API error: ${err.message}. Is the backend running on port 5000?`);
    } finally {
      setLoading(false);
    }
  }, []);

  // Initial load
  useEffect(() => { fetchAll(); }, [fetchAll]);

  // Rule #7: Auto-refresh opportunities (5 min in demo; use 30–60 min in production)
  useEffect(() => {
    const id = setInterval(fetchAll, 5 * 60 * 1000);
    return () => clearInterval(id);
  }, [fetchAll]);

  const pendingCount = history.filter(b => b.result === 'Pending').length;

  return (
    <div className="min-h-screen bg-gray-900 text-gray-100">
      {/* ── Header ───────────────────────────────────────────────────────── */}
      <header className="bg-gray-800 border-b border-gray-700 px-6 py-4">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-white">
              📊 Betting Analysis
            </h1>
            <p className="text-gray-400 text-xs mt-0.5">
              Pre-match · Multi-sport · Edge-first · Kelly sizing
            </p>
          </div>
          <div className="flex items-center gap-4">
            {lastRefresh && (
              <span className="text-gray-500 text-xs">
                Refreshed {lastRefresh.toLocaleTimeString()}
              </span>
            )}
            <button
              onClick={async () => { await refreshOdds(); fetchAll(); }}
              className="px-3 py-1.5 text-xs rounded-lg bg-blue-700 hover:bg-blue-600 transition-colors"
              title="Clear odds cache and fetch fresh data"
            >
              ⟳ Fetch New Odds
            </button>
            <button
              onClick={fetchAll}
              className="px-3 py-1.5 text-xs rounded-lg bg-gray-700 hover:bg-gray-600 transition-colors"
            >
              ↺ Refresh UI
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-6 py-6 space-y-6">
        {/* ── Error banner ─────────────────────────────────────────────── */}
        {error && (
          <div className="bg-red-900 border border-red-600 text-red-200 rounded-xl px-5 py-4 text-sm">
            <strong>Connection Error</strong><br />
            {error}
          </div>
        )}

        {/* ── Bankroll panel (always visible) ──────────────────────────── */}
        <BankrollPanel bankroll={bankroll} />

        {/* ── Main navigation tabs ─────────────────────────────────────── */}
        <div className="flex gap-1 bg-gray-800 p-1 rounded-xl w-fit">
          {MAIN_TABS.map(tab => (
            <button
              key={tab}
              onClick={() => setMainTab(tab)}
              className={`px-5 py-2 rounded-lg text-sm font-semibold transition-colors ${
                mainTab === tab
                  ? 'bg-blue-600 text-white'
                  : 'text-gray-400 hover:text-white'
              }`}
            >
              {tab}
              {tab === 'History' && pendingCount > 0 && (
                <span className="ml-2 bg-orange-500 text-white text-xs rounded-full px-1.5 py-0.5">
                  {pendingCount}
                </span>
              )}
            </button>
          ))}
        </div>

        {/* ── Opportunities tab ─────────────────────────────────────────── */}
        {mainTab === 'Opportunities' && (
          <div className="space-y-4">
            {/* Sport filter tabs */}
            <div className="flex flex-wrap gap-2">
              {SPORTS.map(s => {
                const count = s === 'All'
                  ? opportunities.length
                  : opportunities.filter(o => o.sportType === s).length;
                return (
                  <button
                    key={s}
                    onClick={() => setSport(s)}
                    className={`flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                      sport === s
                        ? 'bg-blue-600 text-white'
                        : 'bg-gray-700 text-gray-300 hover:bg-gray-600'
                    }`}
                  >
                    <span>{SPORT_EMOJI[s]}</span>
                    <span>{s}</span>
                    <span className={`text-xs rounded-full px-1.5 ${
                      sport === s ? 'bg-blue-500' : 'bg-gray-600'
                    }`}>
                      {count}
                    </span>
                  </button>
                );
              })}
            </div>

            {/* Legend */}
            <div className="flex gap-4 text-xs text-gray-500">
              <span className="flex items-center gap-1">
                <span className="w-3 h-3 rounded bg-green-700 inline-block" /> Edge &gt; 10%
              </span>
              <span className="flex items-center gap-1">
                <span className="w-3 h-3 rounded bg-yellow-700 inline-block" /> Edge 5–10%
              </span>
              <span className="flex items-center gap-1">
                <span className="w-3 h-3 rounded bg-red-700 inline-block" /> Edge &gt; 20% (verify!)
              </span>
            </div>

            {loading ? (
              <LoadingSkeleton />
            ) : (
              <OpportunitiesTable
                opportunities={opportunities}
                selectedSport={sport}
                onBetPlaced={fetchAll}
              />
            )}
          </div>
        )}

        {/* ── History tab ───────────────────────────────────────────────── */}
        {mainTab === 'History' && (
          loading ? <LoadingSkeleton /> : (
            <BetHistoryTable
              history={history}
              onResultUpdated={fetchAll}
            />
          )
        )}

        {/* ── Settings tab ─────────────────────────────────────────────── */}
        {mainTab === 'Settings' && (
          <SettingsPanel bankroll={bankroll} />
        )}
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <footer className="text-center text-gray-600 text-xs py-8 mt-8">
        Pre-match only · Half-Kelly sizing · 10 risk rules active ·
        For simulation purposes only
      </footer>
    </div>
  );
}

function LoadingSkeleton() {
  return (
    <div className="space-y-3 animate-pulse">
      {[1, 2, 3, 4].map(i => (
        <div key={i} className="bg-gray-800 rounded-xl h-14" />
      ))}
    </div>
  );
}
