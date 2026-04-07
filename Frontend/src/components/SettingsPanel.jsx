import React from 'react';

/**
 * SettingsPanel — shows the active risk management configuration.
 *
 * All values are read from the backend config (appsettings.json).
 * To make these editable, add a PUT /Betting/settings endpoint on the backend.
 *
 * Explains every risk rule so users understand the system behaviour.
 */
export default function SettingsPanel({ bankroll }) {
  const maxStakePct  = bankroll ? (bankroll.maxStakePerBet / bankroll.totalBankroll * 100).toFixed(0) : '—';
  const dailyPct     = bankroll ? (bankroll.dailyLossLimit  / bankroll.totalBankroll * 100).toFixed(0) : '—';
  const stopLossPct  = bankroll ? (bankroll.stopLossLimit   / bankroll.totalBankroll * 100).toFixed(0) : '—';
  const fmt = (n) => n != null
    ? new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n)
    : '—';

  const rules = [
    {
      id: 1,
      title: 'Pre-Match Only',
      value: '≥ 1 hour before kickoff',
      desc: 'Only matches starting more than 1 hour in the future are considered. Live markets are excluded entirely.',
      color: 'blue',
    },
    {
      id: 2,
      title: 'Edge Threshold',
      value: '≥ 5%',
      desc: 'A bet opportunity is only shown when our Poisson model probability exceeds the bookmaker implied probability by at least 5%.',
      color: 'yellow',
    },
    {
      id: 3,
      title: 'Max Stake Per Bet',
      value: `${maxStakePct}% of bankroll (${fmt(bankroll?.maxStakePerBet)})`,
      desc: 'Half-Kelly criterion is used for sizing, then capped at this percentage. Prevents overbetting on any single event.',
      color: 'green',
    },
    {
      id: 4,
      title: 'Daily Loss Limit',
      value: `${dailyPct}% of bankroll (${fmt(bankroll?.dailyLossLimit)})`,
      desc: 'If cumulative losses today exceed this threshold, no further bets are placed until midnight UTC.',
      color: 'orange',
    },
    {
      id: 5,
      title: 'Drawdown Stop-Loss',
      value: `${stopLossPct}% of bankroll (${fmt(bankroll?.stopLossLimit)})`,
      desc: 'If total cumulative losses exceed this threshold, the entire system halts and opportunities are no longer served.',
      color: 'red',
    },
    {
      id: 6,
      title: 'Market Focus',
      value: 'Mid-tier & less-liquid markets',
      desc: 'Avoid over-hyped EPL main games when bankroll is small. Prefer mid-tier matches where pricing inefficiencies are larger.',
      color: 'purple',
    },
    {
      id: 7,
      title: 'Odds Refresh Frequency',
      value: 'Every 30–60 minutes',
      desc: 'In production, OddsService polls the external odds API on this interval to capture line movement and late team news.',
      color: 'blue',
    },
    {
      id: 8,
      title: 'High Edge Verification',
      value: 'Manual check if Edge > 20%',
      desc: 'Edges above 20% often indicate a model miscalibration or incorrect lambda inputs. A warning is shown and manual review is required.',
      color: 'red',
    },
    {
      id: 9,
      title: 'Full Audit Logging',
      value: 'Every prediction and bet logged',
      desc: 'All predictions, suggested stakes, placed bets, and results are persisted. In production this goes to a database.',
      color: 'gray',
    },
    {
      id: 10,
      title: 'Bankroll Sync',
      value: 'Updated after every result',
      desc: 'When a bet result is recorded (Win/Loss), the bankroll, daily loss counter, and cumulative drawdown are all immediately updated.',
      color: 'green',
    },
  ];

  const borderColors = {
    blue: 'border-blue-500', yellow: 'border-yellow-500', green: 'border-green-500',
    orange: 'border-orange-500', red: 'border-red-500', purple: 'border-purple-500',
    gray: 'border-gray-500',
  };
  const textColors = {
    blue: 'text-blue-400', yellow: 'text-yellow-400', green: 'text-green-400',
    orange: 'text-orange-400', red: 'text-red-400', purple: 'text-purple-400',
    gray: 'text-gray-400',
  };

  return (
    <div className="space-y-4">
      <p className="text-gray-400 text-sm">
        Risk management rules are configured in <code className="bg-gray-700 px-1 rounded text-xs">appsettings.json</code>.
        All 10 rules are active simultaneously.
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {rules.map(rule => (
          <div
            key={rule.id}
            className={`bg-gray-800 rounded-xl p-4 border-l-4 ${borderColors[rule.color]}`}
          >
            <div className="flex items-center gap-2 mb-1">
              <span className="text-gray-500 text-xs font-mono">#{rule.id}</span>
              <h3 className="font-semibold text-white text-sm">{rule.title}</h3>
            </div>
            <p className={`font-bold text-sm mb-2 ${textColors[rule.color]}`}>{rule.value}</p>
            <p className="text-gray-400 text-xs leading-relaxed">{rule.desc}</p>
          </div>
        ))}
      </div>

      <div className="bg-gray-700 rounded-xl p-4 mt-4">
        <h3 className="text-white font-semibold mb-3 text-sm">Poisson Model Info</h3>
        <p className="text-gray-400 text-xs leading-relaxed">
          EPL uses a full Poisson goal grid (0–10 × 0–10 scores, renormalised).
          AFL, NRL, NBA, and Esports use a simplified relative-strength model
          (win probability proportional to lambda ratio, no draw bucket).
          Lambda values are hand-calibrated in the mock data; in production
          they are derived from Dixon–Coles attack/defence ratings or
          Elo-based strength indices per sport.
        </p>
      </div>
    </div>
  );
}
