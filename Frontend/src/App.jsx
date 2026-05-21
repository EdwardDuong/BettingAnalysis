import React, { useState, useEffect, useCallback } from 'react';
import { useTheme } from './hooks/useTheme.js';
import BankrollPanel      from './components/BankrollPanel.jsx';
import OpportunitiesTable from './components/OpportunitiesTable.jsx';
import BetHistoryTable    from './components/BetHistoryTable.jsx';
import SettingsPanel      from './components/SettingsPanel.jsx';
import ParlayPanel        from './components/ParlayPanel.jsx';
import AnalyticsPanel     from './components/AnalyticsPanel.jsx';
import RejectedBetsPanel  from './components/RejectedBetsPanel.jsx';
import Toast              from './components/Toast.jsx';
import { getOpportunities, getHistory, getBankroll, getStats, getParlays, refreshOdds } from './services/api.js';

const SPORTS      = ['All', 'EPL', 'AFL', 'NRL', 'NBA', 'Esports'];
const MAIN_TABS   = ['Opportunities', 'Parlays', 'History', 'Analytics', 'Rejected', 'Settings'];
const SPORT_EMOJI = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮', All: '🌐' };

export default function App() {
  const [opportunities, setOpportunities] = useState([]);
  const [history,       setHistory]       = useState([]);
  const [bankroll,      setBankroll]      = useState(null);
  const [stats,         setStats]         = useState(null);
  const [mainTab,       setMainTab]       = useState('Opportunities');
  const [sport,         setSport]         = useState('All');
  const [loading,       setLoading]       = useState(true);
  const [error,         setError]         = useState(null);
  const [lastRefresh,   setLastRefresh]   = useState(null);
  const [refreshing,    setRefreshing]    = useState(false);
  const [toasts,        setToasts]        = useState([]);
  const [parlayCount,   setParlayCount]   = useState(0);
  const { theme, toggle: toggleTheme } = useTheme();

  const addToast = (message, type = 'info') =>
    setToasts(t => [...t, { id: Date.now(), message, type }]);
  const dismissToast = (id) => setToasts(t => t.filter(x => x.id !== id));

  const fetchAll = useCallback(async () => {
    try {
      const [opps, hist, br, st, parls] = await Promise.all([
        getOpportunities(), getHistory(), getBankroll(), getStats(), getParlays()
      ]);
      setOpportunities(opps);
      setHistory(hist);
      setBankroll(br);
      setStats(st);
      setParlayCount(parls?.length ?? 0);
      setLastRefresh(new Date());
      setError(null);
    } catch (err) {
      setError(`API error: ${err.message} — is the backend running on port 5100?`);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  // Auto-refresh every 5 min
  useEffect(() => {
    const id = setInterval(fetchAll, 5 * 60 * 1000);
    return () => clearInterval(id);
  }, [fetchAll]);

  // Keyboard shortcut: R = fetch new odds
  useEffect(() => {
    const handler = (e) => {
      if (e.key === 'r' && !e.ctrlKey && !e.metaKey && e.target.tagName !== 'INPUT') {
        handleFetchNewOdds();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  const handleFetchNewOdds = async () => {
    setRefreshing(true);
    try { await refreshOdds(); await fetchAll(); }
    finally { setRefreshing(false); }
  };

  const pendingCount  = history.filter(b => b.result === 'Pending').length;
  const goodBetCount  = opportunities.filter(o => o.aiValidation?.decision === 'GOOD_BET').length;
  const riskyCount    = opportunities.filter(o => o.aiValidation?.decision === 'RISKY').length;

  return (
    <div className="min-h-screen bg-gray-900 text-gray-100">
      {/* ── Header ──────────────────────────────────────────────────── */}
      <header className="bg-gray-800 border-b border-gray-700 px-6 py-4">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-white">📊 Betting Analysis</h1>
            <p className="text-gray-400 text-xs mt-0.5">
              Pre-match · up to 2 weeks ahead · Half-Kelly · AI Validated
            </p>
          </div>
          <div className="flex items-center gap-3">
            {/* Quick stats */}
            {stats && (
              <div className="hidden md:flex gap-3 text-xs">
                <Pill label="Win Rate" value={`${stats.winRate}%`} color="text-green-400" />
                {stats.avgCLV != null && (
                  <Pill
                    label="Avg CLV"
                    value={`${stats.avgCLV >= 0 ? '+' : ''}${stats.avgCLV?.toFixed(2)}%`}
                    color={stats.avgCLV >= 2 ? 'text-green-400' : stats.avgCLV >= 0 ? 'text-yellow-400' : 'text-red-400'}
                  />
                )}
                <Pill label="PnL" value={`$${(stats.totalPnL ?? 0).toFixed(0)}`} color={stats.totalPnL >= 0 ? 'text-green-400' : 'text-red-400'} />
                {stats.roi != null && (
                  <Pill
                    label="ROI"
                    value={`${stats.roi >= 0 ? '+' : ''}${stats.roi?.toFixed(1)}%`}
                    color={stats.roi >= 5 ? 'text-green-400' : stats.roi >= 0 ? 'text-yellow-400' : 'text-red-400'}
                  />
                )}
                {stats.currentStreak != null && stats.currentStreak !== 0 && (
                  <Pill
                    label={stats.currentStreak > 0 ? 'W Streak' : 'L Streak'}
                    value={`${Math.abs(stats.currentStreak)}`}
                    color={stats.currentStreak > 0 ? 'text-green-400' : 'text-red-400'}
                  />
                )}
              </div>
            )}
            {lastRefresh && (
              <span className="text-gray-500 text-xs hidden md:block">
                {lastRefresh.toLocaleTimeString()}
              </span>
            )}
            <button onClick={toggleTheme} title="Toggle dark/light mode"
              className="px-3 py-1.5 text-xs rounded-lg bg-gray-700 hover:bg-gray-600 transition-colors">
              {theme === 'dark' ? '☀️' : '🌙'}
            </button>
            <button onClick={handleFetchNewOdds} disabled={refreshing}
              className="px-3 py-1.5 text-xs rounded-lg bg-blue-700 hover:bg-blue-600 disabled:opacity-50 transition-colors">
              {refreshing ? '…' : '⟳ New Odds'}
            </button>
            <button onClick={fetchAll}
              className="px-3 py-1.5 text-xs rounded-lg bg-gray-700 hover:bg-gray-600 transition-colors">
              ↺ Refresh
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-6 py-6 space-y-6">
        {error && (
          <div className="bg-red-900 border border-red-600 text-red-200 rounded-xl px-5 py-4 text-sm">
            <strong>Connection Error</strong><br />{error}
          </div>
        )}

        <BankrollPanel bankroll={bankroll} onReset={fetchAll} />

        {/* ── Main tabs ───────────────────────────────────────────── */}
        <div className="flex gap-1 bg-gray-800 p-1 rounded-xl w-fit">
          {MAIN_TABS.map(tab => (
            <button key={tab} onClick={() => setMainTab(tab)}
              className={`px-5 py-2 rounded-lg text-sm font-semibold transition-colors ${
                mainTab === tab ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'
              }`}>
              {tab}
              {tab === 'History' && pendingCount > 0 && (
                <span className="ml-2 bg-orange-500 text-white text-xs rounded-full px-1.5 py-0.5">{pendingCount}</span>
              )}
              {tab === 'Parlays' && parlayCount > 0 && (
                <span className="ml-2 bg-purple-600 text-white text-xs rounded-full px-1.5 py-0.5">{parlayCount}</span>
              )}
            </button>
          ))}
        </div>

        {/* ── Opportunities ───────────────────────────────────────── */}
        {mainTab === 'Opportunities' && (
          <div className="space-y-4">
            {/* AI summary */}
            {!loading && (
              <div className="flex gap-3 text-sm">
                <span className="bg-green-900 border border-green-700 text-green-300 px-3 py-1 rounded-lg">
                  ✅ {goodBetCount} GOOD BET{goodBetCount !== 1 ? 'S' : ''}
                </span>
                <span className="bg-yellow-900 border border-yellow-700 text-yellow-300 px-3 py-1 rounded-lg">
                  ⚠️ {riskyCount} RISKY
                </span>
                <span className="bg-gray-700 text-gray-300 px-3 py-1 rounded-lg">
                  {opportunities.length - goodBetCount - riskyCount} SKIP
                </span>
              </div>
            )}

            {/* Sport tabs */}
            <div className="flex flex-wrap gap-2">
              {SPORTS.map(s => {
                const cnt = s === 'All' ? opportunities.length
                  : opportunities.filter(o => o.sportType === s).length;
                return (
                  <button key={s} onClick={() => setSport(s)}
                    className={`flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                      sport === s ? 'bg-blue-600 text-white' : 'bg-gray-700 text-gray-300 hover:bg-gray-600'
                    }`}>
                    <span>{SPORT_EMOJI[s]}</span>
                    <span>{s}</span>
                    <span className={`text-xs rounded-full px-1.5 ${sport === s ? 'bg-blue-500' : 'bg-gray-600'}`}>{cnt}</span>
                  </button>
                );
              })}
            </div>

            {/* Legend */}
            <div className="flex flex-wrap gap-4 text-xs text-gray-500">
              <span className="flex items-center gap-1"><span className="w-3 h-3 rounded bg-green-800 inline-block" /> GOOD BET (score ≥ 6, no major flags)</span>
              <span className="flex items-center gap-1"><span className="w-3 h-3 rounded bg-gray-700 inline-block" /> RISKY (1–2 flags)</span>
              <span className="flex items-center gap-1"><span className="w-3 h-3 rounded bg-red-900 inline-block" /> SKIP (blocked — button disabled)</span>
            </div>

            {loading ? <Skeleton /> : (
              <OpportunitiesTable
                opportunities={opportunities}
                selectedSport={sport}
                onBetPlaced={fetchAll}
                onToast={addToast}
              />
            )}
          </div>
        )}

        {/* ── Parlays ─────────────────────────────────────────────── */}
        {mainTab === 'Parlays' && <ParlayPanel />}

        {/* ── History ─────────────────────────────────────────────── */}
        {mainTab === 'History' && (
          loading ? <Skeleton /> : <BetHistoryTable history={history} onResultUpdated={fetchAll} />
        )}

        {/* ── Analytics ───────────────────────────────────────────── */}
        {mainTab === 'Analytics' && <AnalyticsPanel history={history} />}

        {/* ── Rejected ────────────────────────────────────────────── */}
        {mainTab === 'Rejected' && <RejectedBetsPanel />}

        {/* ── Settings ────────────────────────────────────────────── */}
        {mainTab === 'Settings' && <SettingsPanel />}
      </main>

      <Toast toasts={toasts} onDismiss={dismissToast} />

      <footer className="text-center text-gray-600 text-xs py-8">
        Pre-match only · up to 2 weeks ahead · Half-Kelly sizing · AI validated · 11 risk rules active
      </footer>
    </div>
  );
}

function Pill({ label, value, color }) {
  return (
    <div className="bg-gray-700 rounded-lg px-2 py-1 text-center">
      <span className="text-gray-400">{label} </span>
      <span className={`font-bold ${color}`}>{value}</span>
    </div>
  );
}

function Skeleton() {
  return (
    <div className="space-y-3 animate-pulse">
      {[1, 2, 3, 4].map(i => <div key={i} className="bg-gray-800 rounded-xl h-14" />)}
    </div>
  );
}
