import React, { useState } from 'react';
import { placeBet } from '../services/api.js';

const SPORT_EMOJI = {
  EPL: '⚽', LaLiga: '⚽', Bundesliga: '⚽', SerieA: '⚽', Ligue1: '⚽',
  Eredivisie: '⚽', PrimeiraLiga: '⚽', MLS: '⚽', ChampionsLeague: '⚽',
  AFL: '🏈', NRL: '🏉', NBA: '🏀', MLB: '⚾', Esports: '🎮',
};

const DECISION_STYLE = {
  GOOD_BET: { bg: 'bg-green-800',  text: 'text-green-100',  label: '✅ GOOD BET' },
  RISKY:    { bg: 'bg-yellow-800', text: 'text-yellow-100', label: '⚠️ RISKY'    },
  SKIP:     { bg: 'bg-red-900',    text: 'text-red-200',    label: '🚫 SKIP'     },
};

const FLAG_STYLE = {
  HIGH_EDGE:           { bg: 'bg-red-700',    label: '🔴 HIGH EDGE'       },
  LINE_MOVING_AGAINST: { bg: 'bg-red-700',    label: '↑ DRIFTING'         },
  ODDS_TOO_LOW:        { bg: 'bg-orange-700', label: '📉 ODDS LOW'        },
  HIGH_VARIANCE:       { bg: 'bg-orange-700', label: '📈 HIGH VARIANCE'   },
  CORRELATED_BET:      { bg: 'bg-yellow-700', label: '🔗 CORRELATED'      },
  BAD_TIMING:          { bg: 'bg-yellow-700', label: '⏰ BAD TIMING'      },
  EPL_LOW_EDGE:        { bg: 'bg-yellow-700', label: '🔵 EPL THIN EDGE'   },
  STEAMING:            { bg: 'bg-green-700',  label: '↓ STEAMING'         },
};

const SORT_OPTIONS = [
  { value: 'ai',       label: 'AI Score'    },
  { value: 'edge',     label: 'Edge'        },
  { value: 'odds',     label: 'Odds'        },
  { value: 'kickoff',  label: 'Kickoff'     },
  { value: 'stake',    label: 'Stake'       },
];

export default function OpportunitiesTable({ opportunities, selectedSport, onBetPlaced, onToast }) {
  const [recording, setRecording] = useState(null);
  const [feedback,  setFeedback]  = useState({});
  const [sortBy,    setSortBy]    = useState('ai');
  const [search,    setSearch]    = useState('');
  const [expanded,  setExpanded]  = useState({});
  const [minOdds,   setMinOdds]   = useState(0);

  const toggleExpand = (key) => setExpanded(e => ({ ...e, [key]: !e[key] }));

  const filtered = (selectedSport === 'All'
    ? opportunities
    : opportunities.filter(o => o.sportType === selectedSport)
  ).filter(o => !search.trim() ||
    [o.homeTeam, o.awayTeam, o.team].some(s => s?.toLowerCase().includes(search.toLowerCase()))
  ).filter(o => minOdds === 0 || (o.odds ?? 0) >= minOdds)
  .slice().sort((a, b) => {
    switch (sortBy) {
      case 'edge':    return (b.edge ?? 0) - (a.edge ?? 0);
      case 'odds':    return (b.odds ?? 0) - (a.odds ?? 0);
      case 'kickoff': return new Date(a.matchStartTime) - new Date(b.matchStartTime);
      case 'stake':   return (b.suggestedStake ?? 0) - (a.suggestedStake ?? 0);
      default:        // 'ai' — preserve backend sort (GOOD_BET first, then score)
        const decisionOrder = { GOOD_BET: 0, RISKY: 1, SKIP: 2 };
        const da = decisionOrder[a.aiValidation?.decision] ?? 2;
        const db = decisionOrder[b.aiValidation?.decision] ?? 2;
        return da !== db ? da - db : (b.aiValidation?.score ?? 0) - (a.aiValidation?.score ?? 0);
    }
  });

  const handleRecord = async (opp) => {
    const key = `${opp.matchId}-${opp.outcome}`;
    if (opp.aiValidation?.decision === 'SKIP') {
      setFeedback(f => ({ ...f, [key]: '🚫 AI Validator recommends SKIP — not worth recording.' }));
      return;
    }
    setRecording(key);
    try {
      const res = await placeBet(opp.matchId, opp.outcome, null);
      const warn = res.warnings?.length ? ` ⚠️ ${res.warnings[0]}` : '';
      setFeedback(f => ({ ...f, [key]: `✅ Recorded $${res.bet?.stake?.toFixed(2)}${warn}` }));
      onToast?.(`Recorded: ${opp.team} — $${res.bet?.stake?.toFixed(2)}`, 'success');
      onBetPlaced();
    } catch (err) {
      setFeedback(f => ({ ...f, [key]: `❌ ${err.message}` }));
      onToast?.(err.message, 'error');
    } finally {
      setRecording(null);
    }
  };

  if (filtered.length === 0) {
    return (
      <div className="text-center text-gray-500 py-16 text-sm">
        No pre-match opportunities found for {selectedSport === 'All' ? 'any sport' : selectedSport}.
      </div>
    );
  }

  return (
    <div className="space-y-3">
    <div className="flex items-center gap-3">
      <input
        type="text"
        placeholder="Search team…"
        value={search}
        onChange={e => setSearch(e.target.value)}
        className="bg-gray-700 border border-gray-600 rounded-lg px-3 py-1.5 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 w-44"
      />
      <select
        value={minOdds}
        onChange={e => setMinOdds(Number(e.target.value))}
        className="bg-gray-700 border border-gray-600 rounded-lg px-2 py-1.5 text-xs text-gray-300 focus:outline-none focus:border-blue-500"
      >
        <option value={0}>All odds</option>
        <option value={1.5}>≥ 1.50</option>
        <option value={2.0}>≥ 2.00</option>
        <option value={2.5}>≥ 2.50</option>
        <option value={3.0}>≥ 3.00</option>
      </select>
      <span className="text-gray-500 text-xs flex-1">{filtered.length} match{filtered.length !== 1 ? 'es' : ''}</span>
    </div>
    <div className="flex items-center gap-2 justify-end">
      <span className="text-gray-500 text-xs">Sort by:</span>
      {SORT_OPTIONS.map(opt => (
        <button key={opt.value} onClick={() => setSortBy(opt.value)}
          className={`px-2.5 py-1 rounded-lg text-xs transition-colors ${
            sortBy === opt.value ? 'bg-blue-600 text-white' : 'bg-gray-700 text-gray-400 hover:text-white'
          }`}>
          {opt.label}
        </button>
      ))}
    </div>
    <div className="overflow-x-auto rounded-xl border border-gray-700">
      <table className="w-full text-sm text-left">
        <thead>
          <tr className="bg-gray-700 text-gray-300 uppercase text-xs tracking-wide">
            <th className="px-3 py-3">AI</th>
            <th className="px-3 py-3">Score</th>
            <th className="px-3 py-3">Sport</th>
            <th className="px-3 py-3">Match</th>
            <th className="px-3 py-3">Bet</th>
            <th className="px-3 py-3 text-right">Odds</th>
            <th className="px-3 py-3 text-right">Prob</th>
            <th className="px-3 py-3 text-right">EV</th>
            <th className="px-3 py-3 text-right">Stake</th>
            <th className="px-3 py-3">Line</th>
            <th className="px-3 py-3">Flags</th>
            <th className="px-3 py-3">Kickoff</th>
            <th className="px-3 py-3">Action</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-700">
          {filtered.map(opp => {
            const key      = `${opp.matchId}-${opp.outcome}`;
            const ai       = opp.aiValidation;
            const decision = ai?.decision ?? 'RISKY';
            const ds       = DECISION_STYLE[decision] ?? DECISION_STYLE.RISKY;
            const rowBg    = decision === 'GOOD_BET'
              ? 'bg-green-950 hover:bg-green-900'
              : decision === 'SKIP'
              ? 'bg-red-950 hover:bg-red-900 opacity-70'
              : 'bg-gray-800 hover:bg-gray-750';

            return (
              <React.Fragment key={key}>
                <tr className={`${rowBg} transition-colors`}>
                  {/* AI Decision badge */}
                  <td className="px-3 py-3">
                    <span className={`inline-block px-2 py-0.5 rounded text-xs font-bold ${ds.bg} ${ds.text}`}>
                      {ds.label}
                    </span>
                  </td>

                  {/* Score bar */}
                  <td className="px-3 py-3">
                    <ScoreBar score={ai?.score ?? 5} />
                  </td>

                  {/* Sport */}
                  <td className="px-3 py-3 text-lg" title={opp.sportType}>
                    {SPORT_EMOJI[opp.sportType]}
                  </td>

                  {/* Match */}
                  <td className="px-3 py-3 text-white font-medium whitespace-nowrap text-xs">
                    {opp.homeTeam} <span className="text-gray-500">vs</span> {opp.awayTeam}
                  </td>

                  {/* Bet */}
                  <td className="px-3 py-3 text-blue-300 font-semibold whitespace-nowrap">
                    {opp.team}
                    <span className="ml-1 text-gray-500 text-xs">({opp.outcome})</span>
                  </td>

                  {/* Odds */}
                  <td className="px-3 py-3 text-right font-mono">
                    <span className="text-yellow-300">{opp.odds?.toFixed(2)}</span>
                    {opp.previousOdds && (
                      <div className="text-gray-500 text-xs">was {opp.previousOdds?.toFixed(2)}</div>
                    )}
                  </td>

                  {/* Probability */}
                  <td className="px-3 py-3 text-right font-mono text-gray-300">
                    {((opp.probability ?? 0) * 100).toFixed(1)}%
                  </td>

                  {/* Edge + confidence */}
                  <td className="px-3 py-3 text-right">
                    <div className="space-y-1 items-end flex flex-col">
                      <EdgeBadge edge={opp.edge ?? 0} />
                      {opp.confidenceLevel && <ConfidenceBadge level={opp.confidenceLevel} />}
                    </div>
                  </td>

                  {/* Stake */}
                  <td className="px-3 py-3 text-right font-mono text-green-400 font-bold">
                    ${(opp.suggestedStake ?? 0).toFixed(2)}
                  </td>

                  {/* Line movement */}
                  <td className="px-3 py-3 whitespace-nowrap text-xs">
                    <LineTag status={opp.lineMovementStatus} />
                  </td>

                  {/* AI Flags */}
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-1">
                      {(ai?.flags ?? []).map(flag => {
                        const fs = FLAG_STYLE[flag];
                        if (!fs) return null;
                        return (
                          <span key={flag} className={`px-1.5 py-0.5 rounded text-xs font-medium text-white ${fs.bg}`}>
                            {fs.label}
                          </span>
                        );
                      })}
                    </div>
                  </td>

                  {/* Kickoff */}
                  <td className="px-3 py-3 text-xs whitespace-nowrap">
                    <KickoffTime utcTime={opp.matchStartTime} hours={opp.hoursUntilKickoff} />
                  </td>

                  {/* Action */}
                  <td className="px-3 py-3">
                    <button
                      onClick={() => handleRecord(opp)}
                      disabled={recording === key || decision === 'SKIP'}
                      className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition-colors whitespace-nowrap
                        ${decision === 'SKIP'
                          ? 'bg-gray-700 text-gray-500 cursor-not-allowed'
                          : 'bg-blue-600 hover:bg-blue-500 disabled:opacity-50'}`}
                    >
                      {recording === key ? '…' : 'Record Bet'}
                    </button>
                  </td>
                </tr>

                {/* AI reason row */}
                {ai?.reason && (
                  <tr className="bg-gray-900">
                    <td colSpan={13} className="px-3 py-1.5 text-xs text-gray-400 italic">
                      AI: {ai.reason}
                      <button onClick={() => toggleExpand(key)}
                        className="ml-3 text-blue-400 hover:text-blue-300 not-italic">
                        {expanded[key] ? '▲ hide' : '▼ model detail'}
                      </button>
                    </td>
                  </tr>
                )}

                {/* Probability breakdown */}
                {expanded[key] && (
                  <tr className="bg-gray-950">
                    <td colSpan={13} className="px-3 py-2">
                      <ProbBreakdown opp={opp} />
                    </td>
                  </tr>
                )}

                {/* Validation warnings */}
                {(opp.validationWarnings ?? []).map((w, i) => (
                  <tr key={i} className="bg-yellow-950">
                    <td colSpan={13} className="px-3 py-1 text-xs text-yellow-300">⚠️ {w}</td>
                  </tr>
                ))}

                {/* Feedback */}
                {feedback[key] && (
                  <tr className="bg-gray-900">
                    <td colSpan={13} className="px-3 py-1.5 text-xs text-gray-300">{feedback[key]}</td>
                  </tr>
                )}
              </React.Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
    </div>
  );
}

function ScoreBar({ score }) {
  const color = score >= 7 ? 'bg-green-500' : score >= 5 ? 'bg-yellow-500' : 'bg-red-500';
  return (
    <div className="flex items-center gap-1.5">
      <div className="w-16 bg-gray-700 rounded-full h-1.5">
        <div className={`h-1.5 rounded-full ${color}`} style={{ width: `${score * 10}%` }} />
      </div>
      <span className="text-xs text-gray-400 font-mono">{score}/10</span>
    </div>
  );
}

function ConfidenceBadge({ level }) {
  const styles = {
    High:   'bg-emerald-800 text-emerald-200',
    Medium: 'bg-sky-800 text-sky-200',
    Low:    'bg-gray-700 text-gray-400',
  };
  return (
    <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${styles[level] ?? styles.Low}`}>
      {level}
    </span>
  );
}

function EdgeBadge({ edge }) {
  const pct = (edge * 100).toFixed(1);
  if (edge >= 0.20) return <span className="px-2 py-0.5 rounded text-xs font-bold bg-red-700 text-white">{pct}%</span>;
  if (edge >= 0.08) return <span className="px-2 py-0.5 rounded text-xs font-bold bg-green-700 text-white">{pct}%</span>;
  if (edge >= 0.04) return <span className="px-2 py-0.5 rounded text-xs font-bold bg-yellow-700 text-white">{pct}%</span>;
  return <span className="px-2 py-0.5 rounded text-xs font-bold bg-gray-600 text-gray-300">{pct}%</span>;
}

function LineTag({ status }) {
  if (status === 'Steaming') return <span className="text-green-400">↓ Steam</span>;
  if (status === 'Drifting') return <span className="text-red-400">↑ Drift</span>;
  return <span className="text-gray-500">→ Stable</span>;
}

function ProbBreakdown({ opp }) {
  const implied = opp.odds > 0 ? (1 / opp.odds) : 0;
  const model   = opp.probability ?? 0;
  const probGap = model - implied;
  const ev      = opp.edge ?? 0; // stored as EV: model_prob × odds − 1
  return (
    <div className="flex flex-wrap gap-6 text-xs text-gray-400">
      <div>
        <span className="text-gray-500">Model prob </span>
        <span className="text-white font-mono font-bold">{(model * 100).toFixed(1)}%</span>
      </div>
      <div>
        <span className="text-gray-500">Market implied </span>
        <span className="text-white font-mono">{(implied * 100).toFixed(1)}%</span>
      </div>
      <div>
        <span className="text-gray-500">Prob gap </span>
        <span className={`font-mono font-bold ${probGap >= 0 ? 'text-green-400' : 'text-red-400'}`}>
          {probGap >= 0 ? '+' : ''}{(probGap * 100).toFixed(1)}pp
        </span>
      </div>
      <div>
        <span className="text-gray-500">EV </span>
        <span className={`font-mono font-bold ${ev >= 0 ? 'text-green-400' : 'text-red-400'}`}>
          {ev >= 0 ? '+' : ''}{(ev * 100).toFixed(1)}%
        </span>
        <span className="text-gray-600 ml-1 text-xs">per $1</span>
      </div>
      <div>
        <span className="text-gray-500">Odds </span>
        <span className="text-yellow-300 font-mono">{opp.odds?.toFixed(2)}</span>
        {opp.previousOdds && (
          <span className="text-gray-600 ml-1">(was {opp.previousOdds?.toFixed(2)})</span>
        )}
      </div>
    </div>
  );
}

function KickoffTime({ utcTime, hours }) {
  const local   = new Date(utcTime);
  const dateStr = local.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
  const timeStr = local.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  const hLeft   = typeof hours === 'number' ? hours : (local - Date.now()) / 3600000;
  const label   = hLeft >= 24 ? `${Math.floor(hLeft / 24)}d ${Math.floor(hLeft % 24)}h`
    : hLeft >= 1 ? `${hLeft.toFixed(1)}h` : `${Math.round(hLeft * 60)}m`;
  return (
    <div>
      <div className="text-gray-300">{dateStr}</div>
      <div className="text-gray-500">{timeStr} · <span className="text-blue-400">in {label}</span></div>
    </div>
  );
}
