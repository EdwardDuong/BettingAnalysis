import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { getToken } from '../services/api.js';

/**
 * Connects to the BettingHub and registers event handlers.
 * Reconnects automatically. Cleans up on unmount or when disabled.
 *
 * handlers: { EventName: (payload) => void, ... }
 * options.enabled: connect only when true (e.g. pass isAuthenticated())
 */
export function useSignalR(handlers, { enabled = true } = {}) {
  // Ref keeps handlers stable without restarting the connection on every render
  const handlersRef = useRef(handlers);
  handlersRef.current = handlers;

  useEffect(() => {
    if (!enabled) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/betting', {
        accessTokenFactory: () => getToken() ?? '',
      })
      .withAutomaticReconnect([2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    const registeredEvents = Object.keys(handlersRef.current);
    registeredEvents.forEach(event => {
      connection.on(event, (...args) => handlersRef.current[event]?.(...args));
    });

    connection.start().catch(err => {
      console.warn('SignalR initial connect failed:', err?.message ?? err);
    });

    return () => { connection.stop(); };
  }, [enabled]);
}
