import React from 'react';

const fmt = (n) => new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD' }).format(n ?? 0);
const pct = (used, limit) => limit > 0 ? Math.min((used / limit) * 100, 100) : 0;

export default function BankrollPanel({ bankroll }) {
  if (!bankroll) return <div className="bg-gray-800 rounded-xl p-4 animate-pulse h-40" />;

  const dailyPct     = pct(bankroll.dailyLossUsed,   bankroll.dailyLossLimit);
  const drawdownPct  = pct(bankroll.cumulativeLoss,   bankroll.stopLossLimit);
  const exposurePct  = pct(bankroll.totalExposure,    bankroll.maxExposure);
  const availablePct = bankroll.totalBankroll > 0
    ? (bankroll.availableBankroll / bankroll.totalBankroll) * 100 : 100;

  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-5 space-y-4">

      {/* ── System halt alerts ─────────────────────────────────────── */}
      {bankroll.isStopLossTriggered && (
        <Alert color="red" icon="🛑">
          SYSTEM HALTED — Cumulative drawdown {fmt(bankroll.cumulativeLoss)} ≥ stop-loss {fmt(bankroll.stopLossLimit)}.
        </Alert>
      )}
      {bankroll.isTiltProtectionActive && !bankroll.isStopLossTriggered && (
        <Alert color="orange" icon="🧠">
          TILT PROTECTION — {bankroll.consecutiveLosses} consecutive losses. No new bets until you reset.
        </Alert>
      )}
      {bankroll.isDailyLimitReached && !bankroll.isStopLossTriggered && !bankroll.isTiltProtectionActive && (
        <Alert color="yellow" icon="⚠️">
          Daily loss limit reached — {fmt(bankroll.dailyLossUsed)} of {fmt(bankroll.dailyLossLimit)}. Resumes midnight UTC.
        </Alert>
      )}

      {/* ── Stats grid ────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Stat label="Total Bankroll"   value={fmt(bankroll.totalBankroll)}      color="text-white" />
        <Stat label="Available"        value={fmt(bankroll.availableBankroll)}   color="text-green-400" />
        <Stat label="Open Exposure"    value={fmt(bankroll.totalExposure)}       color={bankroll.isExposureLimitReached ? 'text-red-400' : 'text-blue-400'} />
        <Stat label="Max Stake / Bet"  value={fmt(bankroll.maxStakePerBet)}      color="text-purple-400" />
      </div>

      {/* ── Tilt counter ──────────────────────────────────────────── */}
      <div className="flex items-center gap-2 text-sm">
        <span className="text-gray-400">Consecutive losses:</span>
        {Array.from({ length: bankroll.maxConsecutiveLosses }).map((_, i) => (
          <span
            key={i}
            className={`w-4 h-4 rounded-full border ${
              i < bankroll.consecutiveLosses
                ? 'bg-red-500 border-red-500'
                : 'bg-gray-700 border-gray-600'
            }`}
          />
        ))}
        <span className={`text-xs ml-1 ${bankroll.isTiltProtectionActive ? 'text-red-400 font-bold' : 'text-gray-500'}`}>
          {bankroll.consecutiveLosses}/{bankroll.maxConsecutiveLosses}
          {bankroll.isTiltProtectionActive ? ' — TILT PROTECTION ACTIVE' : ''}
        </span>
      </div>

      {/* ── Progress bars ─────────────────────────────────────────── */}
      <div className="space-y-2.5">
        <Bar label="Available Bankroll"    value={availablePct} used={fmt(bankroll.availableBankroll)} total={fmt(bankroll.totalBankroll)}  color={availablePct > 60 ? 'bg-green-500' : availablePct > 30 ? 'bg-yellow-500' : 'bg-red-500'} />
        <Bar label="Daily Loss"            value={dailyPct}     used={fmt(bankroll.dailyLossUsed)}    total={fmt(bankroll.dailyLossLimit)}   color={dailyPct < 60 ? 'bg-blue-500' : dailyPct < 90 ? 'bg-yellow-500' : 'bg-red-600'} />
        <Bar label="Cumulative Drawdown"   value={drawdownPct}  used={fmt(bankroll.cumulativeLoss)}   total={fmt(bankroll.stopLossLimit)}    color={drawdownPct < 60 ? 'bg-purple-500' : drawdownPct < 90 ? 'bg-orange-500' : 'bg-red-600'} />
        <Bar label="Open Exposure"         value={exposurePct}  used={fmt(bankroll.totalExposure)}    total={fmt(bankroll.maxExposure)}      color={exposurePct < 60 ? 'bg-teal-500' : exposurePct < 90 ? 'bg-yellow-500' : 'bg-red-600'} />
      </div>
    </div>
  );
}

function Alert({ color, icon, children }) {
  const styles = {
    red:    'bg-red-900 border-red-500 text-red-200',
    orange: 'bg-orange-900 border-orange-500 text-orange-200',
    yellow: 'bg-yellow-900 border-yellow-600 text-yellow-200',
  };
  return (
    <div className={`border rounded-lg px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${styles[color]}`}>
      <span>{icon}</span>{children}
    </div>
  );
}

function Stat({ label, value, color }) {
  return (
    <div className="bg-gray-700 rounded-lg p-3 text-center">
      <p className="text-gray-400 text-xs mb-1">{label}</p>
      <p className={`font-bold text-base ${color}`}>{value}</p>
    </div>
  );
}

function Bar({ label, value, used, total, color }) {
  return (
    <div>
      <div className="flex justify-between text-xs text-gray-400 mb-1">
        <span>{label}</span>
        <span>{used} / {total} <span className="text-gray-500">({value.toFixed(1)}%)</span></span>
      </div>
      <div className="w-full bg-gray-700 rounded-full h-1.5">
        <div className={`h-1.5 rounded-full transition-all ${color}`} style={{ width: `${value}%` }} />
      </div>
    </div>
  );
}
