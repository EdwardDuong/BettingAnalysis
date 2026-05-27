import React, { useState, useEffect } from 'react';
import { getSettings, saveSettings, changePassword } from '../services/api.js';

export default function SettingsPanel() {
  const [config, setConfig]   = useState(null);
  const [saving, setSaving]   = useState(false);
  const [status, setStatus]   = useState(null);   // { type: 'ok'|'error', msg }
  const [blacklist, setBlacklist] = useState(''); // comma-separated input

  useEffect(() => {
    getSettings()
      .then(cfg => {
        setConfig(cfg);
        setBlacklist((cfg.teamBlacklist ?? []).join(', '));
      })
      .catch(err => setStatus({ type: 'error', msg: err.message }));
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setStatus(null);
    try {
      const updated = {
        ...config,
        teamBlacklist: blacklist.split(',').map(s => s.trim()).filter(Boolean),
      };
      await saveSettings(updated);
      setStatus({ type: 'ok', msg: 'Settings saved — changes apply immediately.' });
    } catch (err) {
      setStatus({ type: 'error', msg: err.message });
    } finally {
      setSaving(false);
    }
  };

  const set = (key, val) => setConfig(c => ({ ...c, [key]: val }));
  const num = (key, val) => set(key, parseFloat(val) || 0);

  if (!config) return <div className="animate-pulse bg-gray-800 rounded-xl h-96" />;

  return (
    <div className="space-y-6 max-w-2xl">
      <ChangePasswordSection />
      {status && (
        <div className={`rounded-lg px-4 py-2.5 text-sm ${
          status.type === 'ok'
            ? 'bg-green-900 border border-green-600 text-green-200'
            : 'bg-red-900 border border-red-600 text-red-200'
        }`}>
          {status.msg}
        </div>
      )}

      <Section title="Edge Rules">
        <Field label="Min Edge Threshold" hint="5% recommended" suffix="%">
          <input type="number" step="0.5" min="1" max="50"
            value={(config.edgeThreshold * 100).toFixed(1)}
            onChange={e => set('edgeThreshold', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
        <Field label="High Edge Flag (suspicious)" hint="20% = possible false edge" suffix="%">
          <input type="number" step="1" min="10" max="50"
            value={(config.highEdgeThreshold * 100).toFixed(0)}
            onChange={e => set('highEdgeThreshold', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
      </Section>

      <Section title="Bet Sizing">
        <Field label="Kelly Fraction" hint="0.5 = half-Kelly (recommended)">
          <select value={config.kellyFraction} onChange={e => num('kellyFraction', e.target.value)} className={input}>
            <option value="0.25">0.25 — Quarter Kelly (conservative)</option>
            <option value="0.5">0.50 — Half Kelly (recommended)</option>
            <option value="0.75">0.75 — Three-quarter Kelly</option>
          </select>
        </Field>
        <Field label="Max Stake Per Bet" hint="Hard cap regardless of Kelly" suffix="% of bankroll">
          <input type="number" step="0.5" min="0.5" max="10"
            value={(config.maxStakePercent * 100).toFixed(1)}
            onChange={e => set('maxStakePercent', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
      </Section>

      <Section title="Bankroll Limits">
        <Field label="Max Exposure" hint="Total open bets" suffix="% of bankroll">
          <input type="number" step="1" min="5" max="30"
            value={(config.maxExposurePercent * 100).toFixed(0)}
            onChange={e => set('maxExposurePercent', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
        <Field label="Daily Loss Limit" hint="Stop for the day" suffix="% of bankroll">
          <input type="number" step="1" min="5" max="25"
            value={(config.dailyLossLimitPercent * 100).toFixed(0)}
            onChange={e => set('dailyLossLimitPercent', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
        <Field label="Stop-Loss (Drawdown)" hint="Halt system" suffix="% of bankroll">
          <input type="number" step="5" min="10" max="50"
            value={(config.stopLossPercent * 100).toFixed(0)}
            onChange={e => set('stopLossPercent', parseFloat(e.target.value) / 100)}
            className={input} />
        </Field>
      </Section>

      <Section title="Timing Window">
        <Field label="Min Hours Before Kickoff" hint="Minimum 1h (no late bets)">
          <input type="number" step="0.5" min="0.5" max="6"
            value={config.preMatchMinHours}
            onChange={e => num('preMatchMinHours', e.target.value)}
            className={input} />
        </Field>
        <Field label="Max Hours Before Kickoff" hint="Maximum 6h (no far-future bets)">
          <input type="number" step="1" min="2" max="24"
            value={config.preMatchMaxHours}
            onChange={e => num('preMatchMaxHours', e.target.value)}
            className={input} />
        </Field>
      </Section>

      <Section title="Discipline Rules">
        <Field label="Tilt Protection" hint="Stop after N consecutive losses">
          <select value={config.maxConsecutiveLosses} onChange={e => set('maxConsecutiveLosses', parseInt(e.target.value))} className={input}>
            {[2,3,4,5].map(n => <option key={n} value={n}>{n} losses</option>)}
          </select>
        </Field>
        <Field label="Max Bets Per Match" hint="Correlation limit">
          <select value={config.maxBetsPerMatch} onChange={e => set('maxBetsPerMatch', parseInt(e.target.value))} className={input}>
            {[1,2,3].map(n => <option key={n} value={n}>{n} bet{n > 1 ? 's' : ''}</option>)}
          </select>
        </Field>
        <div>
          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={config.requireLineMovementCheck ?? true}
              onChange={e => set('requireLineMovementCheck', e.target.checked)}
              className="w-4 h-4 rounded" />
            <span className="text-sm text-gray-300">Block bets when odds are drifting (line movement rule)</span>
          </label>
        </div>
      </Section>

      <Section title="Team Blacklist (Emotional Bias Protection)">
        <Field label="Blacklisted Teams" hint="Comma-separated team names — bets on these teams are blocked">
          <input type="text" placeholder="e.g. Manchester United, Arsenal"
            value={blacklist}
            onChange={e => setBlacklist(e.target.value)}
            className={input} />
        </Field>
      </Section>

      <button
        onClick={handleSave}
        disabled={saving}
        className="w-full py-3 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 rounded-xl font-semibold text-white transition-colors"
      >
        {saving ? 'Saving…' : '💾 Save Settings'}
      </button>

      <p className="text-gray-500 text-xs text-center">
        Changes apply immediately — no server restart needed.
        Odds cache is refreshed automatically when timing window changes.
      </p>
    </div>
  );
}

function Section({ title, children }) {
  return (
    <div className="bg-gray-800 border border-gray-700 rounded-xl p-5 space-y-4">
      <h3 className="text-white font-semibold text-sm border-b border-gray-700 pb-2">{title}</h3>
      {children}
    </div>
  );
}

function Field({ label, hint, suffix, children }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="flex-1">
        <p className="text-gray-300 text-sm">{label} {suffix && <span className="text-gray-500">({suffix})</span>}</p>
        {hint && <p className="text-gray-500 text-xs">{hint}</p>}
      </div>
      <div className="w-56">{children}</div>
    </div>
  );
}

const input = 'w-full bg-gray-700 border border-gray-600 rounded-lg px-3 py-1.5 text-white text-sm focus:outline-none focus:border-blue-500';

function ChangePasswordSection() {
  const [current,  setCurrent]  = useState('');
  const [next,     setNext]     = useState('');
  const [confirm,  setConfirm]  = useState('');
  const [saving,   setSaving]   = useState(false);
  const [status,   setStatus]   = useState(null);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setStatus(null);
    if (next !== confirm) { setStatus({ type: 'error', msg: 'New passwords do not match.' }); return; }
    if (next.length < 8)  { setStatus({ type: 'error', msg: 'New password must be at least 8 characters.' }); return; }
    setSaving(true);
    try {
      await changePassword(current, next);
      setStatus({ type: 'ok', msg: 'Password changed successfully.' });
      setCurrent(''); setNext(''); setConfirm('');
    } catch (err) {
      setStatus({ type: 'error', msg: err.message });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Section title="Change Password">
      {status && (
        <div className={`rounded-lg px-4 py-2.5 text-sm ${
          status.type === 'ok'
            ? 'bg-green-900 border border-green-600 text-green-200'
            : 'bg-red-900 border border-red-600 text-red-200'
        }`}>
          {status.msg}
        </div>
      )}
      <form onSubmit={handleSubmit} className="space-y-3">
        <Field label="Current password" hint="">
          <input type="password" value={current} onChange={e => setCurrent(e.target.value)}
            placeholder="••••••••" required className={input} />
        </Field>
        <Field label="New password" hint="Min 8 characters">
          <input type="password" value={next} onChange={e => setNext(e.target.value)}
            placeholder="••••••••" required className={input} />
        </Field>
        <Field label="Confirm new password" hint="">
          <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)}
            placeholder="••••••••" required className={input} />
        </Field>
        <div className="flex justify-end pt-1">
          <button type="submit" disabled={saving}
            className="px-5 py-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 rounded-lg text-sm font-semibold text-white transition-colors">
            {saving ? 'Saving…' : 'Change Password'}
          </button>
        </div>
      </form>
    </Section>
  );
}
