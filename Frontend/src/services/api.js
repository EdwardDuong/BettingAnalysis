const BASE      = '/Betting';
const AUTH_BASE = '/api/auth';

// ── Auth helpers ──────────────────────────────────────────────────────────────

export function getToken()         { return localStorage.getItem('jwt_token'); }
export function getRefreshToken()  { return localStorage.getItem('jwt_refresh'); }
export function getUser()          { const u = localStorage.getItem('jwt_user'); return u ? JSON.parse(u) : null; }
export function isAuthenticated()  { return !!getToken(); }

function saveTokens(data) {
  localStorage.setItem('jwt_token', data.token);
  if (data.refreshToken) localStorage.setItem('jwt_refresh', data.refreshToken);
  if (data.user)         localStorage.setItem('jwt_user', JSON.stringify(data.user));
}

export async function logout() {
  const rt = getRefreshToken();
  if (rt) {
    // Best-effort server-side revocation — ignore errors
    await fetch(`${AUTH_BASE}/logout`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body:    JSON.stringify({ refreshToken: rt }),
    }).catch(() => {});
  }
  localStorage.removeItem('jwt_token');
  localStorage.removeItem('jwt_refresh');
  localStorage.removeItem('jwt_user');
}

function authHeaders() {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

// Tracks an in-flight refresh to avoid parallel refresh attempts
let _refreshPromise = null;

async function silentRefresh() {
  if (_refreshPromise) return _refreshPromise;

  _refreshPromise = (async () => {
    const rt = getRefreshToken();
    if (!rt) return false;

    const res = await fetch(`${AUTH_BASE}/refresh`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ refreshToken: rt }),
    });

    if (!res.ok) return false;

    const data = await res.json();
    saveTokens(data);
    return true;
  })().finally(() => { _refreshPromise = null; });

  return _refreshPromise;
}

// ── Error extraction ──────────────────────────────────────────────────────────
// Backend can return several shapes; extract the most readable message from any.

function extractMessage(body, status) {
  if (!body) return `Request failed (HTTP ${status})`;

  // Plain JSON string: BadRequest("some message") → "some message"
  if (typeof body === 'string') return body;

  // Bet rejection: { Violations: [...], Warnings: [...] }
  if (Array.isArray(body.violations) && body.violations.length)
    return body.violations.join(' · ');
  if (Array.isArray(body.Violations) && body.Violations.length)
    return body.Violations.join(' · ');

  // Model validation: { title, errors: { field: ["msg"] } }
  if (body.errors && typeof body.errors === 'object') {
    const msgs = Object.values(body.errors).flat();
    if (msgs.length) return msgs.join(' · ');
  }

  // ProblemDetails / GlobalExceptionHandler: { title, detail }
  if (body.detail) return body.detail;
  if (body.title)  return body.title;

  // Auth errors: { error: "..." }
  if (body.error)  return body.error;

  return `Request failed (HTTP ${status})`;
}

// ── Base request ──────────────────────────────────────────────────────────────

async function request(path, options = {}, _isRetry = false) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    ...options,
  });

  if (res.status === 401 && !_isRetry) {
    // Try silent refresh once before giving up
    const refreshed = await silentRefresh();
    if (refreshed) return request(path, options, true);

    // Refresh failed → log out and show expired banner
    sessionStorage.setItem('session_expired', '1');
    await logout();
    window.location.href = '/';
    return;
  }

  if (res.status === 401) {
    // Second 401 after retry means the refresh token is also dead
    sessionStorage.setItem('session_expired', '1');
    await logout();
    window.location.href = '/';
    return;
  }

  if (res.status === 403) {
    throw new Error('Admin access required for this action');
  }

  if (!res.ok) {
    const body = await res.json().catch(() => null) ?? await res.text().catch(() => null);
    throw new Error(extractMessage(body, res.status));
  }
  if (res.status === 204) return null;
  return res.json();
}

// ── Auth API ──────────────────────────────────────────────────────────────────

export async function login(username, password) {
  const res = await fetch(`${AUTH_BASE}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(extractMessage(body, res.status));
  }
  const data = await res.json();
  saveTokens(data);
  return data;
}

export async function register(username, email, password, initialBankroll = 10000) {
  const res = await fetch(`${AUTH_BASE}/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, email, password, initialBankroll }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(extractMessage(body, res.status));
  }
  const data = await res.json();
  saveTokens(data);
  return data;
}

export const getMe = () =>
  fetch(`${AUTH_BASE}/me`, { headers: authHeaders() }).then(r => r.json());

export async function changePassword(currentPassword, newPassword) {
  const res = await fetch(`${AUTH_BASE}/change-password`, {
    method:  'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body:    JSON.stringify({ currentPassword, newPassword }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(extractMessage(body, res.status));
  }
}

export const getOpportunities = () => request('/opportunities');
export const getHistory       = () => request('/history');
export const getBankroll      = () => request('/bankroll');
export const getStats         = () => request('/stats');
export const getSportStats    = () => request('/stats/sport');
export const getParlays       = () => request('/parlays');
export const getRejected      = () => request('/rejected');
export const getSettings      = () => request('/settings');
export const refreshOdds      = () => request('/refresh', { method: 'POST' });

export const placeBet = (matchId, outcome, customStake = null) =>
  request('/place', { method: 'POST', body: JSON.stringify({ matchId, outcome, customStake }) });

export const updateResult = (id, result, closingOdds = null) =>
  request(`/result/${id}`, { method: 'POST', body: JSON.stringify({ result, closingOdds }) });

export const saveSettings = (config) =>
  request('/settings', { method: 'PUT', body: JSON.stringify(config) });

export const resetBankroll = (newAmount = null) =>
  request('/bankroll/reset', { method: 'POST', body: JSON.stringify(newAmount) });

export const getPrediction     = (matchId) => request(`/prediction/${matchId}`);
export const getBankrollHistory = (days = 90) => request(`/bankroll/history?days=${days}`);

export const exportCsv = () => {
  const a = document.createElement('a');
  a.href = `${BASE}/export/csv`;
  a.download = `bet-history-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
};
