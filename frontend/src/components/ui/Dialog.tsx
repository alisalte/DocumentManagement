import { useEffect, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { cx } from './cx';

const maxWidths = {
  sm: 'sm:max-w-sm',
  md: 'sm:max-w-lg',
  lg: 'sm:max-w-2xl',
  xl: 'sm:max-w-4xl',
  full: 'sm:max-w-6xl',
} as const;

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title?: ReactNode;
  children?: ReactNode;
  /** Action buttons, pinned to the bottom. */
  footer?: ReactNode;
  maxWidth?: keyof typeof maxWidths;
  /** Set false for long editors that scroll with the page instead. */
  hideClose?: boolean;
  className?: string;
}

/** Modal dialog: title on top, content in the middle, actions at the bottom. */
export function Dialog({ open, onClose, title, children, footer, maxWidth = 'md', hideClose, className }: DialogProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = previous;
    };
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div className="absolute inset-0 bg-ink-950/50 backdrop-blur-sm animate-[fade-in_0.2s_ease-out]" onClick={onClose} aria-hidden />
      <div
        role="dialog"
        aria-modal="true"
        className={cx(
          'relative flex max-h-[92vh] w-full flex-col overflow-hidden rounded-t-3xl border border-paper-200 bg-paper-50 shadow-[var(--shadow-elevated)] sm:rounded-2xl',
          'animate-[pop-in_0.2s_cubic-bezier(0.2,0.8,0.2,1)]',
          maxWidths[maxWidth],
          className,
        )}
      >
        {title !== undefined && (
          <div className="flex items-center justify-between gap-4 border-b border-paper-200 px-5 py-4 sm:px-6">
            <h2 className="min-w-0 truncate text-base font-bold text-ink-900">{title}</h2>
            {!hideClose && (
              <button
                type="button"
                onClick={onClose}
                aria-label="بستن"
                className="-me-1 size-8 shrink-0 rounded-lg text-paper-400 transition-colors hover:bg-paper-200/70 hover:text-ink-900"
              >
                <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="mx-auto size-4">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
                </svg>
              </button>
            )}
          </div>
        )}
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 sm:px-6">{children}</div>
        {footer && (
          <div className="flex flex-wrap justify-end gap-2 border-t border-paper-200 bg-paper-100/60 px-5 py-3.5 sm:px-6">{footer}</div>
        )}
      </div>
    </div>,
    document.body,
  );
}
