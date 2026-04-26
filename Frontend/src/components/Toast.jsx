import React, { useEffect } from 'react';

const STYLES = {
  success: 'bg-green-800 border-green-600 text-green-100',
  error:   'bg-red-800   border-red-600   text-red-100',
  warning: 'bg-yellow-800 border-yellow-600 text-yellow-100',
  info:    'bg-blue-800  border-blue-600  text-blue-100',
};

export default function Toast({ toasts, onDismiss }) {
  return (
    <div className="fixed bottom-5 right-5 z-50 flex flex-col gap-2 pointer-events-none">
      {toasts.map(t => (
        <ToastItem key={t.id} toast={t} onDismiss={onDismiss} />
      ))}
    </div>
  );
}

function ToastItem({ toast, onDismiss }) {
  useEffect(() => {
    const timer = setTimeout(() => onDismiss(toast.id), toast.duration ?? 4000);
    return () => clearTimeout(timer);
  }, [toast.id, toast.duration, onDismiss]);

  return (
    <div
      className={`pointer-events-auto border rounded-xl px-4 py-3 text-sm font-medium shadow-xl
        flex items-center gap-2 max-w-xs animate-fade-in ${STYLES[toast.type] ?? STYLES.info}`}
      onClick={() => onDismiss(toast.id)}
    >
      <span>{toast.icon ?? defaultIcon(toast.type)}</span>
      <span>{toast.message}</span>
    </div>
  );
}

function defaultIcon(type) {
  return { success: '✅', error: '❌', warning: '⚠️', info: 'ℹ️' }[type] ?? 'ℹ️';
}
