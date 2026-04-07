import React, { useState } from 'react';
import { placeBet } from '../services/api.js';

const SPORT_EMOJI = { EPL: '⚽', AFL: '🏈', NRL: '🏉', NBA: '🏀', Esports: '🎮' };

/**
 * OpportunitiesTable — shows pre-match value bets filtered by sport tab.
 *
 * Colour coding:
 *   - Edge > 20%: red badge + verification warning (Rule #8)
 *   - Edge > 10%: green row highlight (Rule #8 / frontend requirement)
 *   - Edge 5–10%: normal yellow badge
 *
 * Sort: always edge descending (best value first).
 * Times: MatchStartTime converted from UTC to user's local timezone.
 *
 * Rule #1: Backend already excluded matches < 1h away. Frontend shows the remaining.
 * Rule #2: Backend already excluded edge < 5%. All rows shown here have a positive edge.
 */
export default function OpportunitiesTable({ opportunities, selectedSport, onBetPlaced }) {
  const [placing, setPlacing]   = useState(null);  // matchId+outcome being processed
  const [feedback, setFeedback] = useState({});     // { [matchId+outcome]: string }

  const filtered = selectedSport === 'All'
    ? opportunities
    : opportunities.filter(o => o.sportType === selectedSport);

  const handlePlace = async (opp) => {
    const key = `${opp.matchId}-${opp.outcome}`;
    setPlacing(key);
    setFeedback(f => ({ ...f, [key]: null }));

    try {
      const res = await placeBet(opp.matchId, opp.outcome, null);
      const warning = res.warning
        ? ` ⚠️ ${res.warning}`
        : '';
      setFeedback(f => ({ ...f, [key]: `✅ Bet placed: $${res.bet.stake.toFixed(2)}${warning}` }));
      onBetPlaced();
    } catch (err) {
      setFeedback(f => ({ ...f, [key]: `❌ ${err.message}` }));
    } finally {
      setPlacing(null);
    }
  };

  if (filtered.length === 0) {
    return (
      <div className="text-center text-gray-500 py-16 text-sm">
        No value opportunities found for {selectedSport === 'All' ? 'any sport' : selectedSport}.
        <br />
        <span className="text-xs">Pre-match filter is active — only matches starting &gt;1 hour from now are shown.</span>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-xl border border-gray-700">
      <table className="w-full text-sm text-left">
        <thead>
          <tr className="bg-gray-700 text-gray-300 uppercase text-xs">
            <th className="px-4 py-3">Sport</th>
            <th className="px-4 py-3">Match</th>
            <th className="px-4 py-3">Bet On</th>
            <th className="px-4 py-3 text-right">Odds</th>
            <th className="px-4 py-3 text-right">Model Prob</th>
            <th className="px-4 py-3 text-right">Edge</th>
            <th className="px-4 py-3 text-right">Suggested Stake</th>
            <th className="px-4 py-3">Kickoff (Local)</th>
            <th className="px-4 py-3">Action</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-700">
          {filtered.map(opp => {
            const key         = `${opp.matchId}-${opp.outcome}`;
            const isLoading   = placing === key;
            const rowFeedback = feedback[key];

            // Row highlight: green for edge >10%, standard otherwise
            const rowClass = opp.isHighEdge
              ? 'bg-green-950 hover:bg-green-900 transition-colors'
              : 'bg-gray-800 hover:bg-gray-750 transition-colors';

            return (
              <React.Fragment key={key}>
                <tr className={rowClass}>
                  {/* Sport */}
                  <td className="px-4 py-3">
                    <span className="text-lg" title={opp.sportType}>
                      {SPORT_EMOJI[opp.sportType] ?? '🏆'}
                    </span>
                    <span className="ml-1 text-gray-400 text-xs">{opp.sportType}</span>
                  </td>

                  {/* Match */}
                  <td className="px-4 py-3 font-medium text-white whitespace-nowrap">
                    {opp.homeTeam} <span className="text-gray-500">vs</span> {opp.awayTeam}
                  </td>

                  {/* Bet target */}
                  <td className="px-4 py-3">
                    <span className="font-semibold text-blue-300">{opp.team}</span>
                    <span className="ml-1 text-gray-500 text-xs">({opp.outcome})</span>
                  </td>

                  {/* Odds */}
                  <td className="px-4 py-3 text-right font-mono text-yellow-300">
                    {opp.odds.toFixed(2)}
                  </td>

                  {/* Model probability */}
                  <td className="px-4 py-3 text-right font-mono text-gray-300">
                    {(opp.probability * 100).toFixed(1)}%
                  </td>

                  {/* Edge */}
                  <td className="px-4 py-3 text-right">
                    <EdgeBadge edge={opp.edge} requiresVerification={opp.requiresVerification} />
                  </td>

                  {/* Suggested stake */}
                  <td className="px-4 py-3 text-right font-mono text-green-400 font-semibold">
                    ${opp.suggestedStake.toFixed(2)}
                  </td>

                  {/* Kickoff time in local timezone (Rule: display in local tz) */}
                  <td className="px-4 py-3 whitespace-nowrap text-gray-300 text-xs">
                    <KickoffTime utcTime={opp.matchStartTime} />
                  </td>

                  {/* Place bet button */}
                  <td className="px-4 py-3">
                    <button
                      onClick={() => handlePlace(opp)}
                      disabled={isLoading}
                      className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-blue-600 hover:bg-blue-500
                                 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      {isLoading ? 'Placing…' : 'Place Bet'}
                    </button>
                  </td>
                </tr>

                {/* Inline feedback row */}
                {rowFeedback && (
                  <tr className="bg-gray-900">
                    <td colSpan={9} className="px-4 py-2 text-xs text-gray-300">
                      {rowFeedback}
                    </td>
                  </tr>
                )}

                {/* Rule #8: Verification warning for edge > 20% */}
                {opp.requiresVerification && (
                  <tr className="bg-red-950">
                    <td colSpan={9} className="px-4 py-2 text-xs text-red-300 italic">
                      ⚠️ Rule #8: Edge &gt;20% — manually verify Poisson inputs before placing.
                    </td>
                  </tr>
                )}
              </React.Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function EdgeBadge({ edge, requiresVerification }) {
  const pct = (edge * 100).toFixed(1);
  if (requiresVerification) {
    return (
      <span className="inline-block px-2 py-0.5 rounded text-xs font-bold bg-red-700 text-red-100">
        {pct}% ⚠️
      </span>
    );
  }
  if (edge >= 0.10) {
    return (
      <span className="inline-block px-2 py-0.5 rounded text-xs font-bold bg-green-700 text-green-100">
        {pct}%
      </span>
    );
  }
  return (
    <span className="inline-block px-2 py-0.5 rounded text-xs font-bold bg-yellow-700 text-yellow-100">
      {pct}%
    </span>
  );
}

function KickoffTime({ utcTime }) {
  // Convert UTC ISO string to user's local timezone
  const local = new Date(utcTime);
  const dateStr = local.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
  const timeStr = local.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

  // Time until kickoff
  const minsUntil = Math.round((local - Date.now()) / 60000);
  const hoursUntil = Math.floor(minsUntil / 60);
  const label = hoursUntil >= 24
    ? `${Math.floor(hoursUntil / 24)}d ${hoursUntil % 24}h`
    : hoursUntil > 0
    ? `${hoursUntil}h ${minsUntil % 60}m`
    : `${minsUntil}m`;

  return (
    <div>
      <div className="font-medium">{dateStr}</div>
      <div className="text-gray-500">{timeStr} · <span className="text-blue-400">in {label}</span></div>
    </div>
  );
}
