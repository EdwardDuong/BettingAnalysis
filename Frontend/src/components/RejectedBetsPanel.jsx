import React, { useState, useEffect } from 'react';
import { getRejected } from '../services/api.js';

export default function RejectedBetsPanel() {
  const [rejected, setRejected] = useState([]);
  const [loading,  setLoading]  = useState(true);
  const [error,    setError]    = useState(null);

  useEffect(() => {
    getRejected()
      .then(setRejected)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading)
    return <div className="text-gray-500 text-sm text-center py-8 animate-pulse">Loading rejected bets…</div>;

  if (error)
    return <div className="text-red-400 text-sm text-center py-8">{error}</div>;

  if (rejected.length === 0)
    return (
      <div className="bg-gray-800 border border-gray-700 rounded-xl p-8 text-center text-gray-500 text-sm">
        No bets rejected yet — all placed bets passed the 11-rule validation gate.
      </div>
    );

  return (
    <div className="space-y-4">
      <div className="text-sm text-gray-400">
        {rejected.length} rejected bet{rejected.length !== 1 ? 's' : ''} — blocked by the validation gate
      </div>
      <div className="overflow-x-auto rounded-xl border border-gray-700">
        <table className="w-full text-sm text-left">
          <thead>
            <tr className="bg-gray-700 text-gray-300 uppercase text-xs">
              <th className="px-4 py-3">Time</th>
              <th className="px-4 py-3">Match</th>
              <th className="px-4 py-3">Bet</th>
              <th className="px-4 py-3">Violations</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-700">
            {rejected.map((r, i) => (
              <tr key={i} className="bg-gray-800 hover:bg-gray-750 transition-colors">
                <td className="px-4 py-3 text-gray-400 text-xs whitespace-nowrap">
                  {new Date(r.timestamp).toLocaleString()}
                </td>
                <td className="px-4 py-3 text-white text-xs font-mono">{r.matchId}</td>
                <td className="px-4 py-3 text-blue-300 whitespace-nowrap">
                  {r.team} <span className="text-gray-500 text-xs">({r.outcome})</span>
                </td>
                <td className="px-4 py-3">
                  <ul className="space-y-0.5">
                    {r.reasons.map((reason, j) => (
                      <li key={j} className="text-red-300 text-xs flex items-start gap-1">
                        <span className="text-red-500 mt-0.5 shrink-0">✕</span>
                        {reason}
                      </li>
                    ))}
                  </ul>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
