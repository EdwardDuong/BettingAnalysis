/**
 * API Service — all calls to the .NET Betting API backend.
 *
 * Base URL uses Vite proxy (/Betting → http://localhost:5000/Betting).
 * Switch to absolute URL if running without the proxy.
 */

const BASE = '/Betting';

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });

  if (!res.ok) {
    // Try to extract the error message from the response body
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `HTTP ${res.status}`);
  }

  // 204 No Content
  if (res.status === 204) return null;

  return res.json();
}

/**
 * GET /Betting/opportunities
 * Returns pre-match value bets sorted by edge descending.
 * Filtered to Rule #1 (pre-match only) and Rule #2 (edge >= 5%) by the backend.
 */
export const getOpportunities = () => request('/opportunities');

/**
 * POST /Betting/place
 * Simulate placing a bet. Backend enforces all risk rules.
 *
 * @param {string} matchId   - MatchOdds.MatchId
 * @param {string} outcome   - "Home" | "Draw" | "Away"
 * @param {number|null} customStake - Optional override stake in dollars
 */
export const placeBet = (matchId, outcome, customStake = null) =>
  request('/place', {
    method: 'POST',
    body: JSON.stringify({ matchId, outcome, customStake }),
  });

/**
 * GET /Betting/history
 * Returns all placed bets, most recent first.
 */
export const getHistory = () => request('/history');

/**
 * GET /Betting/bankroll
 * Returns current bankroll state including limit flags.
 */
export const getBankroll = () => request('/bankroll');

/**
 * POST /Betting/result/{id}
 * Mark a pending bet as "Win" or "Loss" and update bankroll.
 * Rule #10: bankroll update happens server-side.
 *
 * @param {string} id     - BetHistory.Id (GUID)
 * @param {string} result - "Win" | "Loss"
 */
export const updateResult = (id, result) =>
  request(`/result/${id}`, {
    method: 'POST',
    body: JSON.stringify(result),
  });

/**
 * GET /Betting/stats
 * Returns aggregate win/loss/PnL statistics.
 */
export const getStats = () => request('/stats');

/**
 * POST /Betting/refresh
 * Clears the server-side odds cache so next fetch pulls fresh data from the API.
 */
export const refreshOdds = () => request('/refresh', { method: 'POST' });
