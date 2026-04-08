const BASE = '/Betting';

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `HTTP ${res.status}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

export const getOpportunities = () => request('/opportunities');
export const getHistory       = () => request('/history');
export const getBankroll      = () => request('/bankroll');
export const getStats         = () => request('/stats');
export const getRejected      = () => request('/rejected');
export const getSettings      = () => request('/settings');
export const refreshOdds      = () => request('/refresh', { method: 'POST' });

export const placeBet = (matchId, outcome, customStake = null) =>
  request('/place', { method: 'POST', body: JSON.stringify({ matchId, outcome, customStake }) });

export const updateResult = (id, result, closingOdds = null) =>
  request(`/result/${id}`, { method: 'POST', body: JSON.stringify({ result, closingOdds }) });

export const saveSettings = (config) =>
  request('/settings', { method: 'PUT', body: JSON.stringify(config) });
