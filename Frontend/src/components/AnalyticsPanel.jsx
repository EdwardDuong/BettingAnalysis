import React, { useState, useEffect } from 'react';
import { getSportStats, getBankrollHistory, getCalibrationReport } from '../services/api.js';

const SPORT_EMOJI = {
  EPL: '⚽', LaLiga: '⚽', Bundesliga: '⚽', SerieA: '⚽', Ligue1: '⚽',
  Eredivisie: '⚽', PrimeiraLiga: '⚽', MLS: '⚽', ChampionsLeague: '⚽',
  AFL: '🏈', NRL: '🏉', NBA: '🏀', MLB: '⚾', Esports: '🎮',
};
const fmt = (n) => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n ?? 0);

export default function AnalyticsPanel({ history }) {
  const [sportStats,       setSportStats]       = useState([]);
  const [bankrollHistory,  setBankrollHistory]  = useState([]);
  const [calibration,      setCalibration]      = useState([]);
  const [historyDays,      setHistoryDays]      = useState(90);
  const [loading,          setLoading]          = useState(true);
  const [error,            setError]            = useState(null);

  useEffect(() => {
    Promise.all([getSportStats(), getBankrollHistory(historyDays), getCalibrationReport()])
      .then(([sports, bkHistory, calib]) => { setSportStats(sports); setBankrollHistory(bkHistory); setCalibration(calib); })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, [historyDays]);

  const settled     = (history ?? []).filter(b => b.result !== 'Pending');
  const pnlTimeline = buildPnlTimeline(settled);
  const roi7d       = rollingROI(settled, 7);
  const roi30d      = rollingROI(settled, 30);
  const totalStaked = settled.reduce((s, b) => s + (b.stake ?? 0), 0);
  const avgStake    = settled.length > 0 ? totalStaked / settled.length : 0;

  return (
    <div className="space-y-6">
      {/* ── Stake summary cards ───────────────────────────── */}
      {settled.length > 0 && (
        <div className="grid grid-cols-3 gap-3">
          <div className="bg-gray-700 rounded-xl p-4 text-center">
            <p className="text-gray-400 text-xs mb-1">Avg Stake</p>
            <p className="font-bold text-lg text-white">{fmt(avgStake)}</p>
          </div>
          <div className="bg-gray-700 rounded-xl p-4 text-center">
            <p className="text-gray-400 text-xs mb-1">Total Staked</p>
            <p className="font-bold text-lg text-blue-300">{fmt(totalStaked)}</p>
          </div>
          <div className="bg-gray-700 rounded-xl p-4 text-center">
            <p className="text-gray-400 text-xs mb-1">Avg Edge</p>
            <p className="font-bold text-lg text-yellow-300">
              {settled.length > 0
                ? `${(settled.reduce((s, b) => s + (b.edge ?? 0), 0) / settled.length * 100).toFixed(1)}%`
                : '—'}
            </p>
          </div>
        </div>
      )}

      {/* ── Rolling ROI cards ─────────────────────────────── */}
      {(roi7d !== null || roi30d !== null) && (
        <div className="grid grid-cols-2 gap-3">
          <RoiCard label="7-day ROI" value={roi7d} />
          <RoiCard label="30-day ROI" value={roi30d} />
        </div>
      )}

      {/* ── Bankroll balance history ──────────────────────── */}
      <Section
        title="Bankroll Balance History"
        action={
          <select
            value={historyDays}
            onChange={e => setHistoryDays(Number(e.target.value))}
            className="bg-gray-700 border border-gray-600 text-gray-300 text-xs rounded px-2 py-1"
          >
            <option value={30}>30 days</option>
            <option value={90}>90 days</option>
            <option value={180}>180 days</option>
            <option value={365}>1 year</option>
          </select>
        }
      >
        {bankrollHistory.length === 0
          ? <p className="text-gray-500 text-sm text-center py-6">No bankroll history yet.</p>
          : <BankrollChart data={bankrollHistory} />}
      </Section>

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

      {/* ── Model calibration ─────────────────────────────── */}
      <Section title="Model Calibration (predicted vs. actual)">
        <CalibrationTable buckets={calibration} />
      </Section>

      {/* ── Edge distribution ─────────────────────────────── */}
      <Section title="Edge Distribution (settled bets)">
        <EdgeDistribution bets={settled} />
      </Section>
    </div>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

function Section({ title, action, children }) {
  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-5 space-y-4">
      <div className="flex items-center justify-between border-b border-gray-700 pb-2">
        <h3 className="text-white font-semibold text-sm">{title}</h3>
        {action}
      </div>
      {children}
    </div>
  );
}

function BankrollChart({ data }) {
  const w = 600, h = 100, pad = 8;
  const vals   = data.map(d => Number(d.bankroll));
  const min    = Math.min(...vals) * 0.98;
  const max    = Math.max(...vals) * 1.02;
  const range  = max - min || 1;
  const first  = vals[0];
  const last   = vals[vals.length - 1];
  const up     = last >= first;

  const toX = (i) => pad + (i / Math.max(vals.length - 1, 1)) * (w - pad * 2);
  const toY = (v) => h - pad - ((v - min) / range) * (h - pad * 2);

  const points    = vals.map((v, i) => `${toX(i)},${toY(v)}`).join(' ');
  const fillPath  = `M${toX(0)},${h} ` + vals.map((v, i) => `L${toX(i)},${toY(v)}`).join(' ') + ` L${toX(vals.length - 1)},${h} Z`;
  const color     = up ? '#22c55e' : '#ef4444';
  const fillColor = up ? 'rgba(34,197,94,0.12)' : 'rgba(239,68,68,0.12)';
  const fmt       = (n) => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 }).format(n);

  return (
    <div className="space-y-2">
      <div className="flex justify-between text-xs text-gray-400 mb-1">
        <span>Start: <span className="text-white font-medium">{fmt(first)}</span></span>
        <span className={`font-bold ${up ? 'text-green-400' : 'text-red-400'}`}>
          {up ? '▲' : '▼'} {fmt(Math.abs(last - first))} ({((last - first) / first * 100).toFixed(1)}%)
        </span>
        <span>Now: <span className="text-white font-medium">{fmt(last)}</span></span>
      </div>
      <svg viewBox={`0 0 ${w} ${h}`} className="w-full h-24" preserveAspectRatio="none">
        <path d={fillPath} fill={fillColor} />
        <polyline points={points} fill="none" stroke={color} strokeWidth="2" />
        <circle cx={toX(vals.length - 1)} cy={toY(last)} r="3" fill={color} />
      </svg>
      <div className="flex justify-between text-xs text-gray-500">
        <span>{data[0]?.date}</span>
        <span>{data[data.length - 1]?.date}</span>
      </div>
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

function CalibrationTable({ buckets }) {
  if (!buckets || buckets.length === 0)
    return (
      <p className="text-gray-500 text-sm text-center py-4">
        No settled bets yet — this fills in once results start coming in.
        A well-calibrated model's "60–70%" bucket should show an actual win
        rate near 60–70%; a large, consistent gap means the model (or a
        sport's calibration factor) is biased.
      </p>
    );

  const gapColor = (gap) => {
    const abs = Math.abs(gap);
    if (abs <= 5)  return 'text-green-400';
    if (abs <= 15) return 'text-yellow-400';
    return 'text-red-400';
  };

  return (
    <div className="space-y-2">
      <div className="overflow-x-auto">
        <table className="w-full text-sm text-left">
          <thead>
            <tr className="text-gray-400 text-xs uppercase border-b border-gray-700">
              <th className="pb-2">Predicted Bucket</th>
              <th className="pb-2 text-right">Sample Size</th>
              <th className="pb-2 text-right">Predicted Avg</th>
              <th className="pb-2 text-right">Actual Win Rate</th>
              <th className="pb-2 text-right">Gap</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {buckets.map(b => (
              <tr key={b.bucket} className="hover:bg-gray-700 transition-colors">
                <td className="py-3 font-medium text-white">{b.bucket}</td>
                <td className="py-3 text-right text-gray-300">
                  {b.sampleSize}
                  {b.sampleSize < 10 && <span className="text-gray-500 text-xs ml-1">(small sample)</span>}
                </td>
                <td className="py-3 text-right text-gray-300">{b.predictedAvgPct?.toFixed(1)}%</td>
                <td className="py-3 text-right text-gray-300">{b.actualWinRatePct?.toFixed(1)}%</td>
                <td className={`py-3 text-right font-bold font-mono ${gapColor(b.gapPct)}`}>
                  {b.gapPct >= 0 ? '+' : ''}{b.gapPct?.toFixed(1)}%
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="text-gray-500 text-xs">
        Gap = actual win rate − predicted average. Green ≤5pp, yellow ≤15pp, red &gt;15pp.
        Buckets with few settled bets are noisy — treat them as a signal to
        keep watching, not a verdict.
      </p>
    </div>
  );
}

function RoiCard({ label, value }) {
  if (value === null) return null;
  const color = value >= 5 ? 'text-green-400' : value >= 0 ? 'text-yellow-400' : 'text-red-400';
  return (
    <div className="bg-gray-700 rounded-xl p-4 text-center">
      <p className="text-gray-400 text-xs mb-1">{label}</p>
      <p className={`font-bold text-xl ${color}`}>
        {value >= 0 ? '+' : ''}{value.toFixed(1)}%
      </p>
    </div>
  );
}

function rollingROI(bets, days) {
  const cutoff = Date.now() - days * 86400000;
  const recent = bets.filter(b => new Date(b.dateTimePlaced) >= cutoff);
  if (!recent.length) return null;
  const staked = recent.reduce((s, b) => s + (b.stake ?? 0), 0);
  const pnl    = recent.reduce((s, b) => s + (b.pnL   ?? 0), 0);
  return staked > 0 ? (pnl / staked) * 100 : 0;
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
