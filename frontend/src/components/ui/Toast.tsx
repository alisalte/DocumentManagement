import { useEffect } from 'react';
import type { ReactNode } from 'react';
import { cx } from './cx';

export interface ToastProps {
  open: boolean;
  message: ReactNode;
  onClose: () => void;
  /** Milliseconds before it disappears on its own; 0 keeps it until dismissed. */
  autoHideDuration?: number;
  severity?: 'neutral' | 'success' | 'error';
  action?: ReactNode;
}

/** Transient confirmation, bottom of the screen. */
export function Toast({ open, message, onClose, autoHideDuration = 4000, severity = 'neutral', action }: ToastProps) {
  useEffect(() => {
    if (!open || autoHideDuration <= 0) return;
    const handle = setTimeout(onClose, autoHideDuration);
    return () => clearTimeout(handle);
  }, [open, autoHideDuration, onClose]);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex justify-center p-4 sm:p-6" role="status">
      <div
        className={cx(
          'pointer-events-auto flex max-w-md items-center gap-4 rounded-xl px-4 py-3 text-sm text-white shadow-lg',
          'animate-[fade-in_0.25s_ease-out]',
          severity === 'success' && 'bg-emerald-700',
          severity === 'error' && 'bg-rose-700',
          severity === 'neutral' && 'bg-ink-900',
        )}
      >
        <span className="min-w-0 flex-1">{message}</span>
        {action}
        <button
          type="button"
          onClick={onClose}
          aria-label="بستن"
          className="-me-1 size-6 shrink-0 rounded-md text-white/70 hover:bg-white/10 hover:text-white"
        >
          <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="mx-auto size-4">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      </div>
    </div>
  );
}
