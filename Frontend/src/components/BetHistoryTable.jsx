import React, { useState } from 'react';
import { updateResult } from '../services/api.js';

const SPORT_EMOJI = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮' };

/**
 * BetHistoryTable — displays all placed bets.
 *
 * Features:
 *   - Win/Loss/Pending status badge
 *   - PnL in green (profit) or red (loss)
 *   - "Mark Win / Mark Loss" buttons for Pending bets
 *   - Aggregate summary row at bottom
 *   - Rule #9: all bet data is fetched from the logging service
 *   - Rule #10: marking a result triggers bankroll update server-side
 */
export default function BetHistoryTable({ history, onResultUpdated }) {
  const [updating, setUpdating] = useState(null);
  const [error, setError]       = useState(null);

  const handleResult = async (id, result) => {
    setUpdating(id);
    setError(null);
    try {
      await updateResult(id, result);
      onResultUpdated();
    } catch (err) {
      setError(err.message);
    } finally {
      setUpdating(null);
    }
  };

  const settled    = history.filter(b => b.result !== 'Pending');
  const totalPnL   = settled.reduce((acc, b) => acc + b.pnL, 0);
  const wins       = settled.filter(b => b.result === 'Win').length;
  const winRate    = settled.length > 0 ? (wins / settled.length * 100).toFixed(1) : '—';

  const fmt = (n) =>
    new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n);

  if (history.length === 0) {
    return (
      <div className="text-center text-gray-500 py-16 text-sm">
        No bets placed yet. Find an opportunity above and click "Place Bet".
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {error && (
        <div className="bg-red-900 border border-red-600 text-red-200 rounded-lg px-4 py-2 text-sm">
          {error}
        </div>
      )}

      {/* Summary bar */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <SummaryCard label="Total Bets"  value={history.length}       color="text-white" />
        <SummaryCard label="Win Rate"    value={`${winRate}%`}        color="text-green-400" />
        <SummaryCard label="Settled"     value={settled.length}       color="text-blue-400" />
        <SummaryCard
          label="Total PnL"
          value={fmt(totalPnL)}
          color={totalPnL >= 0 ? 'text-green-400' : 'text-red-400'}
        />
      </div>

      <div className="overflow-x-auto rounded-xl border border-gray-700">
        <table className="w-full text-sm text-left">
          <thead>
            <tr className="bg-gray-700 text-gray-300 uppercase text-xs">
              <th className="px-4 py-3">Sport</th>
              <th className="px-4 py-3">Match</th>
              <th className="px-4 py-3">Bet</th>
              <th className="px-4 py-3 text-right">Odds</th>
              <th className="px-4 py-3 text-right">Edge</th>
              <th className="px-4 py-3 text-right">Stake</th>
              <th className="px-4 py-3">Placed</th>
              <th className="px-4 py-3 text-center">Result</th>
              <th className="px-4 py-3 text-right">PnL</th>
              <th className="px-4 py-3">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {history.map(bet => (
              <tr key={bet.id} className="bg-gray-800 hover:bg-gray-750 transition-colors">
                <td className="px-4 py-3 text-lg" title={bet.sportType}>
                  {SPORT_EMOJI[bet.sportType] ?? '🏆'}
                </td>
                <td className="px-4 py-3 text-white whitespace-nowrap">
                  {bet.homeTeam} <span className="text-gray-500">vs</span> {bet.awayTeam}
                </td>
                <td className="px-4 py-3 text-blue-300 font-medium">
                  {bet.team}
                  <span className="ml-1 text-gray-500 text-xs">({bet.outcome})</span>
                </td>
                <td className="px-4 py-3 text-right font-mono text-yellow-300">
                  {bet.odds.toFixed(2)}
                </td>
                <td className="px-4 py-3 text-right font-mono text-gray-300">
                  {(bet.edge * 100).toFixed(1)}%
                </td>
                <td className="px-4 py-3 text-right font-mono font-semibold text-white">
                  ${bet.stake.toFixed(2)}
                </td>
                <td className="px-4 py-3 text-gray-400 text-xs whitespace-nowrap">
                  {new Date(bet.dateTimePlaced).toLocaleString()}
                </td>
                <td className="px-4 py-3 text-center">
                  <ResultBadge result={bet.result} />
                </td>
                <td className="px-4 py-3 text-right font-mono font-bold">
                  {bet.result === 'Pending' ? (
                    <span className="text-gray-500">—</span>
                  ) : (
                    <span className={bet.pnL >= 0 ? 'text-green-400' : 'text-red-400'}>
                      {bet.pnL >= 0 ? '+' : ''}{fmt(bet.pnL)}
                    </span>
                  )}
                </td>
                <td className="px-4 py-3">
                  {bet.result === 'Pending' && (
                    <div className="flex gap-1">
                      <button
                        onClick={() => handleResult(bet.id, 'Win')}
                        disabled={updating === bet.id}
                        className="px-2 py-1 text-xs rounded bg-green-700 hover:bg-green-600 disabled:opacity-50 transition-colors"
                      >
                        Win
                      </button>
                      <button
                        onClick={() => handleResult(bet.id, 'Loss')}
                        disabled={updating === bet.id}
                        className="px-2 py-1 text-xs rounded bg-red-700 hover:bg-red-600 disabled:opacity-50 transition-colors"
                      >
                        Loss
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function ResultBadge({ result }) {
  const styles = {
    Win:     'bg-green-800 text-green-200',
    Loss:    'bg-red-800 text-red-200',
    Pending: 'bg-gray-600 text-gray-300',
  };
  return (
    <span className={`inline-block px-2 py-0.5 rounded text-xs font-semibold ${styles[result]}`}>
      {result}
    </span>
  );
}

function SummaryCard({ label, value, color }) {
  return (
    <div className="bg-gray-700 rounded-lg p-3 text-center">
      <p className="text-gray-400 text-xs mb-1">{label}</p>
      <p className={`font-bold text-lg ${color}`}>{value}</p>
    </div>
  );
}
