import React, { useState, useEffect } from 'react';
import { getSportStats } from '../services/api.js';

const SPORT_EMOJI = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮' };
const fmt = (n) => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n ?? 0);

export default function AnalyticsPanel({ history }) {
  const [sportStats, setSportStats] = useState([]);
  const [loading,    setLoading]    = useState(true);
  const [error,      setError]      = useState(null);

  useEffect(() => {
    getSportStats()
      .then(setSportStats)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  const settled = (history ?? []).filter(b => b.result !== 'Pending');

  // Build a 30-day cumulative PnL series from history
  const pnlTimeline = buildPnlTimeline(settled);

  return (
    <div className="space-y-6">
      {/* ── Cumulative PnL timeline ────────────────────────── */}
      <Section title="Cumulative P&L (last 30 days)">
        {pnlTimeline.length === 0
          ? <p className="text-gray-500 text-sm text-center py-6">No settled bets yet.</p>
          : <PnlChart timeline={pnlTimeline} />}
      </Section>

      {/* ── Per-sport breakdown ───────────────────────────── */}
      <Section title="Performance by Sport">
        {loading ? (
          <div className="animate-pulse space-y-2">
            {[1, 2, 3].map(i => <div key={i} className="bg-gray-700 h-10 rounded" />)}
          </div>
        ) : error ? (
          <p className="text-red-400 text-sm">{error}</p>
        ) : sportStats.length === 0 ? (
          <p className="text-gray-500 text-sm text-center py-6">No settled bets yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm text-left">
              <thead>
                <tr className="text-gray-400 text-xs uppercase border-b border-gray-700">
                  <th className="pb-2">Sport</th>
                  <th className="pb-2 text-right">Bets</th>
                  <th className="pb-2 text-right">Wins</th>
                  <th className="pb-2 text-right">Win Rate</th>
                  <th className="pb-2 text-right">Avg Edge</th>
                  <th className="pb-2 text-right">Avg CLV</th>
                  <th className="pb-2 text-right">Total P&L</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-700">
                {sportStats.map(s => (
                  <tr key={s.sport} className="hover:bg-gray-700 transition-colors">
                    <td className="py-3 flex items-center gap-2">
                      <span>{SPORT_EMOJI[s.sport] ?? '🏆'}</span>
                      <span className="font-medium text-white">{s.sport}</span>
                    </td>
                    <td className="py-3 text-right text-gray-300">{s.total}</td>
                    <td className="py-3 text-right text-gray-300">{s.wins}</td>
                    <td className="py-3 text-right">
                      <WinRateBar rate={s.winRate} />
                    </td>
                    <td className="py-3 text-right text-gray-300">{s.avgEdge?.toFixed(1)}%</td>
                    <td className="py-3 text-right">
                      {s.avgCLV != null
                        ? <span className={s.avgCLV >= 0 ? 'text-green-400' : 'text-red-400'}>
                            {s.avgCLV >= 0 ? '+' : ''}{s.avgCLV?.toFixed(2)}%
                          </span>
                        : <span className="text-gray-500">—</span>}
                    </td>
                    <td className={`py-3 text-right font-bold font-mono ${s.totalPnL >= 0 ? 'text-green-400' : 'text-red-400'}`}>
                      {s.totalPnL >= 0 ? '+' : ''}{fmt(s.totalPnL)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>

      {/* ── Edge distribution ─────────────────────────────── */}
      <Section title="Edge Distribution (settled bets)">
        <EdgeDistribution bets={settled} />
      </Section>
    </div>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

function Section({ title, children }) {
  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-5 space-y-4">
      <h3 className="text-white font-semibold text-sm border-b border-gray-700 pb-2">{title}</h3>
      {children}
    </div>
  );
}

function WinRateBar({ rate }) {
  const color = rate >= 55 ? 'bg-green-500' : rate >= 40 ? 'bg-yellow-500' : 'bg-red-500';
  return (
    <div className="flex items-center gap-2 justify-end">
      <span className="text-gray-300 text-xs w-10 text-right">{rate?.toFixed(1)}%</span>
      <div className="w-20 bg-gray-700 rounded-full h-1.5">
        <div className={`h-1.5 rounded-full ${color}`} style={{ width: `${Math.min(rate, 100)}%` }} />
      </div>
    </div>
  );
}

function PnlChart({ timeline }) {
  // Simple SVG sparkline
  const w = 600, h = 80, pad = 8;
  const vals = timeline.map(p => p.cumulative);
  const min  = Math.min(0, ...vals);
  const max  = Math.max(0, ...vals);
  const range = max - min || 1;

  const toX = (i) => pad + (i / (vals.length - 1)) * (w - pad * 2);
  const toY = (v) => h - pad - ((v - min) / range) * (h - pad * 2);

  const points = vals.map((v, i) => `${toX(i)},${toY(v)}`).join(' ');
  const zeroY  = toY(0);
  const final  = vals[vals.length - 1];

  return (
    <div className="space-y-2">
      <svg viewBox={`0 0 ${w} ${h}`} className="w-full h-20" preserveAspectRatio="none">
        {/* Zero line */}
        <line x1={pad} y1={zeroY} x2={w - pad} y2={zeroY}
          stroke="#4B5563" strokeWidth="1" strokeDasharray="4,4" />
        {/* PnL line */}
        <polyline points={points} fill="none"
          stroke={final >= 0 ? '#22c55e' : '#ef4444'} strokeWidth="2" />
        {/* Dots at each point */}
        {vals.map((v, i) => (
          <circle key={i} cx={toX(i)} cy={toY(v)} r="2"
            fill={v >= 0 ? '#22c55e' : '#ef4444'} />
        ))}
      </svg>
      <div className="flex justify-between text-xs text-gray-500">
        <span>{timeline[0]?.date}</span>
        <span className={`font-bold ${final >= 0 ? 'text-green-400' : 'text-red-400'}`}>
          {final >= 0 ? '+' : ''}{new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(final)}
        </span>
        <span>{timeline[timeline.length - 1]?.date}</span>
      </div>
    </div>
  );
}

function EdgeDistribution({ bets }) {
  const buckets = [
    { label: '5–7%',   min: 0.05, max: 0.07 },
    { label: '7–10%',  min: 0.07, max: 0.10 },
    { label: '10–15%', min: 0.10, max: 0.15 },
    { label: '15–20%', min: 0.15, max: 0.20 },
    { label: '20%+',   min: 0.20, max: Infinity },
  ];

  const maxCount = Math.max(1, ...buckets.map(b =>
    bets.filter(x => x.edge >= b.min && x.edge < b.max).length));

  if (bets.length === 0)
    return <p className="text-gray-500 text-sm text-center py-4">No settled bets yet.</p>;

  return (
    <div className="space-y-2">
      {buckets.map(b => {
        const matching = bets.filter(x => x.edge >= b.min && x.edge < b.max);
        const wins     = matching.filter(x => x.result === 'Win').length;
        const pct      = (matching.length / maxCount) * 100;
        return (
          <div key={b.label} className="flex items-center gap-3 text-xs">
            <span className="text-gray-400 w-14 text-right">{b.label}</span>
            <div className="flex-1 bg-gray-700 rounded-full h-3 relative">
              <div className="h-3 rounded-full bg-blue-600" style={{ width: `${pct}%` }} />
            </div>
            <span className="text-gray-300 w-20">
              {matching.length} bets {matching.length > 0 ? `(${((wins / matching.length) * 100).toFixed(0)}% W)` : ''}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function buildPnlTimeline(settled) {
  if (settled.length === 0) return [];
  const sorted = [...settled].sort((a, b) => new Date(a.dateTimePlaced) - new Date(b.dateTimePlaced));
  let cumulative = 0;
  return sorted.map(b => {
    cumulative += b.pnL ?? 0;
    return {
      date: new Date(b.dateTimePlaced).toLocaleDateString(),
      pnl: b.pnL ?? 0,
      cumulative,
    };
  });
}
