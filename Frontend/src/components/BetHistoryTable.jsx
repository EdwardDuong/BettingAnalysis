import React, { useState } from 'react';
import { updateResult, exportCsv } from '../services/api.js';

const SPORT_EMOJI = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮' };
const fmt = (n) => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n ?? 0);

export default function BetHistoryTable({ history, onResultUpdated }) {
  const [updating,    setUpdating]    = useState(null);
  const [closingOdds, setClosingOdds] = useState({});
  const [error,       setError]       = useState(null);
  const [search,      setSearch]      = useState('');

  const handleResult = async (id, result) => {
    setUpdating(id);
    setError(null);
    try {
      const odds = parseFloat(closingOdds[id]) || null;
      await updateResult(id, result, odds > 0 ? odds : null);
      onResultUpdated();
    } catch (err) {
      setError(err.message);
    } finally {
      setUpdating(null);
    }
  };

  const displayed = search.trim()
    ? history.filter(b =>
        [b.homeTeam, b.awayTeam, b.team].some(s => s?.toLowerCase().includes(search.toLowerCase())))
    : history;

  const settled   = displayed.filter(b => b.result !== 'Pending');
  const totalPnL  = settled.reduce((s, b) => s + (b.pnL ?? 0), 0);
  const wins      = settled.filter(b => b.result === 'Win').length;
  const clvBets   = settled.filter(b => b.clv != null);
  const avgCLV    = clvBets.length ? clvBets.reduce((s, b) => s + b.clv, 0) / clvBets.length : null;
  const winRate   = settled.length ? (wins / settled.length * 100).toFixed(1) : '—';

  const pendingCount = history.filter(b => b.result === 'Pending').length;

  if (history.length === 0)
    return <div className="text-center text-gray-500 py-16 text-sm">No bets placed yet.</div>;

  return (
    <div className="space-y-4">
      {error && <div className="bg-red-900 border border-red-600 text-red-200 rounded-lg px-4 py-2 text-sm">{error}</div>}

      {/* Summary + search + export */}
      <div className="flex items-center gap-3">
        <input
          type="text"
          placeholder="Search team…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="bg-gray-700 border border-gray-600 rounded-lg px-3 py-1.5 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 w-48"
        />
        <span className="text-gray-500 text-xs flex-1">
          {displayed.length} of {history.length} bet{history.length !== 1 ? 's' : ''}
          {pendingCount > 0 && (
            <span className="ml-2 bg-orange-600 text-white text-xs rounded-full px-1.5 py-0.5">
              {pendingCount} pending
            </span>
          )}
        </span>
        <button
          onClick={exportCsv}
          className="px-3 py-1.5 text-xs rounded-lg bg-gray-700 hover:bg-gray-600 text-gray-300 transition-colors"
        >
          ↓ Export CSV
        </button>
      </div>

      {/* Summary */}
      <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
        <Card label="Total Bets"  value={history.length}                   color="text-white" />
        <Card label="Win Rate"    value={`${winRate}%`}                    color="text-green-400" />
        <Card label="Settled"     value={settled.length}                   color="text-blue-400" />
        <Card label="Total PnL"   value={fmt(totalPnL)}                    color={totalPnL >= 0 ? 'text-green-400' : 'text-red-400'} />
        <Card
          label="Avg CLV"
          value={avgCLV != null ? `${avgCLV >= 0 ? '+' : ''}${avgCLV.toFixed(2)}%` : 'N/A'}
          color={avgCLV == null ? 'text-gray-400' : avgCLV >= 2 ? 'text-green-400' : avgCLV >= 0 ? 'text-yellow-400' : 'text-red-400'}
          tooltip="Closing Line Value — positive = you beat the market long-term"
        />
      </div>

      <div className="overflow-x-auto rounded-xl border border-gray-700">
        <table className="w-full text-sm text-left">
          <thead>
            <tr className="bg-gray-700 text-gray-300 uppercase text-xs">
              <th className="px-3 py-3">Sport</th>
              <th className="px-3 py-3">Match</th>
              <th className="px-3 py-3">Bet</th>
              <th className="px-3 py-3 text-right">Odds</th>
              <th className="px-3 py-3 text-right">Close</th>
              <th className="px-3 py-3 text-right">CLV</th>
              <th className="px-3 py-3 text-right">Edge</th>
              <th className="px-3 py-3 text-right">Stake</th>
              <th className="px-3 py-3">Line</th>
              <th className="px-3 py-3">Placed</th>
              <th className="px-3 py-3 text-center">Result</th>
              <th className="px-3 py-3 text-right">PnL</th>
              <th className="px-3 py-3">Mark Result</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {displayed.map(bet => {
              const clvRowBg = bet.clv != null && bet.result !== 'Pending'
                ? bet.clv >= 3  ? 'bg-green-950 hover:bg-green-900'
                : bet.clv < 0   ? 'bg-red-950 hover:bg-red-900'
                : 'bg-gray-800 hover:bg-gray-750'
                : 'bg-gray-800 hover:bg-gray-750';
              return (
              <tr key={bet.id} className={`${clvRowBg} transition-colors`}>
                <td className="px-3 py-3 text-lg">{SPORT_EMOJI[bet.sportType] ?? '🏆'}</td>

                <td className="px-3 py-3 text-white whitespace-nowrap text-xs">
                  {bet.homeTeam} <span className="text-gray-500">vs</span> {bet.awayTeam}
                </td>

                <td className="px-3 py-3 text-blue-300 font-medium whitespace-nowrap">
                  {bet.team} <span className="text-gray-500 text-xs">({bet.outcome})</span>
                </td>

                <td className="px-3 py-3 text-right font-mono text-yellow-300">{bet.odds?.toFixed(2)}</td>

                {/* Closing odds */}
                <td className="px-3 py-3 text-right font-mono text-gray-400">
                  {bet.closingOdds != null ? bet.closingOdds.toFixed(2) : '—'}
                </td>

                {/* CLV */}
                <td className="px-3 py-3 text-right font-mono font-bold">
                  {bet.clv != null ? (
                    <span className={bet.clv >= 0 ? 'text-green-400' : 'text-red-400'}>
                      {bet.clv >= 0 ? '+' : ''}{bet.clv.toFixed(2)}%
                    </span>
                  ) : <span className="text-gray-500">—</span>}
                </td>

                <td className="px-3 py-3 text-right font-mono text-gray-300">{((bet.edge ?? 0) * 100).toFixed(1)}%</td>

                <td className="px-3 py-3 text-right font-mono font-bold text-white">${(bet.stake ?? 0).toFixed(2)}</td>

                <td className="px-3 py-3 text-xs">
                  {bet.lineMovementStatus === 'Steaming' ? <span className="text-green-400">↓ Steam</span>
                  : bet.lineMovementStatus === 'Drifting' ? <span className="text-red-400">↑ Drift</span>
                  : <span className="text-gray-500">→ Stable</span>}
                </td>

                <td className="px-3 py-3 text-gray-400 text-xs whitespace-nowrap">
                  {new Date(bet.dateTimePlaced).toLocaleString()}
                </td>

                <td className="px-3 py-3 text-center"><ResultBadge result={bet.result} /></td>

                <td className="px-3 py-3 text-right font-mono font-bold">
                  {bet.result === 'Pending' ? <span className="text-gray-500">—</span>
                    : <span className={bet.pnL >= 0 ? 'text-green-400' : 'text-red-400'}>
                        {bet.pnL >= 0 ? '+' : ''}{fmt(bet.pnL)}
                      </span>}
                </td>

                {/* Mark result with optional closing odds */}
                <td className="px-3 py-3">
                  {bet.result === 'Pending' ? (
                    <div className="flex flex-col gap-1 min-w-[140px]">
                      <input
                        type="number"
                        placeholder="Closing odds"
                        step="0.01"
                        className="bg-gray-700 border border-gray-600 rounded px-2 py-0.5 text-xs text-white w-full"
                        value={closingOdds[bet.id] ?? ''}
                        onChange={e => setClosingOdds(o => ({ ...o, [bet.id]: e.target.value }))}
                      />
                      <div className="flex gap-1">
                        <button onClick={() => handleResult(bet.id, 'Win')}  disabled={updating === bet.id}
                          className="flex-1 px-2 py-1 text-xs rounded bg-green-700 hover:bg-green-600 disabled:opacity-50">Win</button>
                        <button onClick={() => handleResult(bet.id, 'Loss')} disabled={updating === bet.id}
                          className="flex-1 px-2 py-1 text-xs rounded bg-red-700 hover:bg-red-600 disabled:opacity-50">Loss</button>
                      </div>
                    </div>
                  ) : null}
                </td>
              </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function ResultBadge({ result }) {
  const s = { Win: 'bg-green-800 text-green-200', Loss: 'bg-red-800 text-red-200', Pending: 'bg-gray-600 text-gray-300' };
  return <span className={`inline-block px-2 py-0.5 rounded text-xs font-semibold ${s[result]}`}>{result}</span>;
}

function Card({ label, value, color, tooltip }) {
  return (
    <div className="bg-gray-700 rounded-lg p-3 text-center" title={tooltip}>
      <p className="text-gray-400 text-xs mb-1">{label}</p>
      <p className={`font-bold text-base ${color}`}>{value}</p>
    </div>
  );
}
