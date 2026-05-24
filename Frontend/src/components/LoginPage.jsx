import { useState } from 'react';
import { login, register } from '../services/api.js';

export default function LoginPage({ onLogin }) {
  const [mode,     setMode]    = useState('login'); // 'login' | 'register'
  const [username, setUsername] = useState('');
  const [email,    setEmail]   = useState('');
  const [password, setPassword] = useState('');
  const [bankroll, setBankroll] = useState('10000');
  const [error,    setError]   = useState(null);
  const [loading,  setLoading] = useState(false);

  const sessionExpired = sessionStorage.getItem('session_expired') === '1';
  if (sessionExpired) sessionStorage.removeItem('session_expired');

  const toggle = () => { setMode(m => m === 'login' ? 'register' : 'login'); setError(null); };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      if (mode === 'login') {
        await login(username, password);
      } else {
        const amount = parseFloat(bankroll);
        if (isNaN(amount) || amount <= 0) { setError('Initial bankroll must be a positive number.'); return; }
        await register(username, email, password, amount);
      }
      onLogin();
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gray-900 flex flex-col items-center justify-center px-4">
      <div className="w-full max-w-sm">
        {/* Logo / title */}
        <div className="text-center mb-8">
          <div className="text-4xl mb-3">📊</div>
          <h1 className="text-2xl font-bold text-white">Betting Analysis</h1>
          <p className="text-gray-400 text-sm mt-1">AI-powered edge finder</p>
        </div>

        {/* Card */}
        <div className="bg-gray-800 border border-gray-700 rounded-2xl p-8 shadow-xl">
          <h2 className="text-lg font-semibold text-white mb-6">
            {mode === 'login' ? 'Sign in' : 'Create account'}
          </h2>

          {sessionExpired && (
            <div className="bg-yellow-900/60 border border-yellow-700 text-yellow-300 text-sm rounded-lg px-4 py-3 mb-4">
              Your session expired. Please sign in again.
            </div>
          )}

          {error && (
            <div className="bg-red-900/60 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-5">
              {error}
            </div>
          )}

          <form onSubmit={handleSubmit} className="space-y-4">
            <Field
              label="Username"
              type="text"
              value={username}
              onChange={setUsername}
              placeholder="your-username"
              autoFocus
            />

            {mode === 'register' && (
              <Field
                label="Email"
                type="email"
                value={email}
                onChange={setEmail}
                placeholder="you@example.com"
              />
            )}

            <Field
              label="Password"
              type="password"
              value={password}
              onChange={setPassword}
              placeholder={mode === 'register' ? 'Min 8 characters' : '••••••••'}
            />

            {mode === 'register' && (
              <Field
                label="Starting bankroll ($)"
                type="number"
                value={bankroll}
                onChange={setBankroll}
                placeholder="10000"
                min="1"
                step="100"
              />
            )}

            <button
              type="submit"
              disabled={loading}
              className="w-full mt-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-semibold py-2.5 rounded-lg transition-colors"
            >
              {loading ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account'}
            </button>
          </form>

          <p className="text-center text-sm text-gray-500 mt-6">
            {mode === 'login' ? "Don't have an account?" : 'Already have an account?'}{' '}
            <button onClick={toggle} className="text-blue-400 hover:text-blue-300 font-medium">
              {mode === 'login' ? 'Register' : 'Sign in'}
            </button>
          </p>
        </div>
      </div>
    </div>
  );
}

function Field({ label, type, value, onChange, placeholder, ...rest }) {
  return (
    <div>
      <label className="block text-xs font-medium text-gray-400 mb-1">{label}</label>
      <input
        type={type}
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        required
        className="w-full bg-gray-700 border border-gray-600 text-white placeholder-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
        {...rest}
      />
    </div>
  );
}
