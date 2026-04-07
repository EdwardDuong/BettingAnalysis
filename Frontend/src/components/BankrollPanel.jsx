import React from 'react';

/**
 * BankrollPanel — displays current bankroll state and risk limit progress bars.
 *
 * Visual indicators:
 *   - Daily loss bar: yellow warning at 70%, red critical at 100%
 *   - Cumulative loss bar: orange warning at 70%, red at stop-loss trigger
 *   - Alert banners if daily limit or stop-loss is triggered
 */
export default function BankrollPanel({ bankroll }) {
  if (!bankroll) {
    return (
      <div className="bg-gray-800 rounded-xl p-4 animate-pulse h-32" />
    );
  }

  const dailyPct       = bankroll.dailyLossLimit > 0
    ? (bankroll.dailyLossUsed / bankroll.dailyLossLimit) * 100
    : 0;
  const stopLossPct    = bankroll.stopLossLimit > 0
    ? (bankroll.cumulativeLoss / bankroll.stopLossLimit) * 100
    : 0;
  const availablePct   = bankroll.totalBankroll > 0
    ? (bankroll.availableBankroll / bankroll.totalBankroll) * 100
    : 100;

  const fmt = (n) =>
    new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n);

  const pctFmt = (n) => `${n.toFixed(1)}%`;

  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-5 space-y-4">
      {/* Stop-loss alert — Rule #5 */}
      {bankroll.isStopLossTriggered && (
        <div className="bg-red-900 border border-red-500 text-red-200 rounded-lg px-4 py-3 font-semibold text-sm flex items-center gap-2">
          <span className="text-lg">🛑</span>
          SYSTEM HALTED — Stop-loss triggered. Cumulative drawdown exceeds {fmt(bankroll.stopLossLimit)}.
        </div>
      )}

      {/* Daily limit alert — Rule #4 */}
      {bankroll.isDailyLimitReached && !bankroll.isStopLossTriggered && (
        <div className="bg-orange-900 border border-orange-500 text-orange-200 rounded-lg px-4 py-3 font-semibold text-sm flex items-center gap-2">
          <span className="text-lg">⚠️</span>
          Daily loss limit reached — no more bets today. Resets at midnight UTC.
        </div>
      )}

      {/* Stats grid */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Stat label="Total Bankroll"  value={fmt(bankroll.totalBankroll)}     color="text-white" />
        <Stat label="Available"       value={fmt(bankroll.availableBankroll)} color="text-green-400" />
        <Stat label="Max Stake / Bet" value={fmt(bankroll.maxStakePerBet)}    color="text-blue-400" />
        <Stat label="Daily Loss Limit" value={fmt(bankroll.dailyLossLimit)}   color="text-yellow-400" />
      </div>

      {/* Progress bars */}
      <div className="space-y-3">
        {/* Available bankroll */}
        <ProgressBar
          label="Available Bankroll"
          value={availablePct}
          used={fmt(bankroll.availableBankroll)}
          total={fmt(bankroll.totalBankroll)}
          colorClass={availablePct > 70 ? 'bg-green-500' : availablePct > 40 ? 'bg-yellow-500' : 'bg-red-500'}
        />

        {/* Daily loss — Rule #4 */}
        <ProgressBar
          label="Daily Loss Used"
          value={Math.min(dailyPct, 100)}
          used={fmt(bankroll.dailyLossUsed)}
          total={fmt(bankroll.dailyLossLimit)}
          colorClass={dailyPct < 70 ? 'bg-blue-500' : dailyPct < 100 ? 'bg-yellow-500' : 'bg-red-600'}
        />

        {/* Cumulative drawdown — Rule #5 */}
        <ProgressBar
          label="Cumulative Drawdown (Stop-Loss)"
          value={Math.min(stopLossPct, 100)}
          used={fmt(bankroll.cumulativeLoss)}
          total={fmt(bankroll.stopLossLimit)}
          colorClass={stopLossPct < 70 ? 'bg-purple-500' : stopLossPct < 100 ? 'bg-orange-500' : 'bg-red-600'}
        />
      </div>
    </div>
  );
}

function Stat({ label, value, color }) {
  return (
    <div className="bg-gray-700 rounded-lg p-3 text-center">
      <p className="text-gray-400 text-xs mb-1">{label}</p>
      <p className={`font-bold text-lg ${color}`}>{value}</p>
    </div>
  );
}

function ProgressBar({ label, value, used, total, colorClass }) {
  return (
    <div>
      <div className="flex justify-between text-xs text-gray-400 mb-1">
        <span>{label}</span>
        <span>{used} / {total} ({value.toFixed(1)}%)</span>
      </div>
      <div className="w-full bg-gray-700 rounded-full h-2">
        <div
          className={`h-2 rounded-full transition-all duration-500 ${colorClass}`}
          style={{ width: `${Math.min(value, 100)}%` }}
        />
      </div>
    </div>
  );
}
