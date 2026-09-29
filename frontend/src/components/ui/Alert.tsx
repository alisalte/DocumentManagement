import type { HTMLAttributes, ReactNode } from 'react';
import { cx } from './cx';

export type AlertSeverity = 'error' | 'success' | 'warning' | 'info';

const toneClasses: Record<AlertSeverity, string> = {
  error: 'border-rose-200 bg-rose-50 text-rose-800',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  warning: 'border-amber-200 bg-amber-50 text-amber-800',
  info: 'border-ink-200 bg-ink-50 text-ink-800',
};

const iconClasses: Record<AlertSeverity, string> = {
  error: 'text-rose-500',
  success: 'text-emerald-500',
  warning: 'text-amber-500',
  info: 'text-ink-600',
};

function Icon({ severity }: { severity: AlertSeverity }) {
  const paths: Record<AlertSeverity, ReactNode> = {
    error: <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v4m0 4h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z" />,
    success: <path strokeLinecap="round" strokeLinejoin="round" d="m9 12 2 2 4-4m6 2a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />,
    warning: <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v4m0 4h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z" />,
    info: <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />,
  };
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden className={cx('size-5 shrink-0', iconClasses[severity])}>
      {paths[severity]}
    </svg>
  );
}

export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
  severity?: AlertSeverity;
  /** Buttons or links shown on the trailing side. */
  action?: ReactNode;
  children?: ReactNode;
}

/** Inline message block. */
export function Alert({ severity = 'info', action, className, children, ...rest }: AlertProps) {
  return (
    <div {...rest} role="alert" className={cx('flex flex-wrap items-start gap-3 rounded-xl border px-4 py-3 text-sm', toneClasses[severity], className)}>
      <Icon severity={severity} />
      <div className="min-w-0 flex-1">{children}</div>
      {action && <div className="flex items-center">{action}</div>}
    </div>
  );
}
