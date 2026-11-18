import type { MouseEvent, ReactNode } from 'react';
import { cx } from './cx';

export type ChipColor = 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'error';

const filledClasses: Record<ChipColor, string> = {
  default: 'bg-paper-200/70 text-paper-700',
  primary: 'bg-ink-50 text-ink-700 ring-1 ring-inset ring-ink-200/70',
  secondary: 'bg-copper-100 text-copper-800',
  success: 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200/70 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/20',
  warning: 'bg-amber-50 text-amber-800 ring-1 ring-inset ring-amber-200/70 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-500/20',
  error: 'bg-rose-50 text-rose-700 ring-1 ring-inset ring-rose-200/70 dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-500/20',
};

const outlinedClasses: Record<ChipColor, string> = {
  default: 'border border-paper-300 bg-paper-50 text-paper-600 tabular-nums',
  primary: 'border border-ink-200 bg-paper-50 text-ink-700',
  secondary: 'border border-copper-200 bg-paper-50 text-copper-700',
  success: 'border border-emerald-200 bg-paper-50 text-emerald-700',
  warning: 'border border-amber-200 bg-paper-50 text-amber-700',
  error: 'border border-rose-200 bg-paper-50 text-rose-700',
};

export interface ChipProps {
  label?: ReactNode;
  color?: ChipColor;
  variant?: 'filled' | 'outlined';
  size?: 'small' | 'medium';
  /** Renders a remove button when provided. */
  onDelete?: (event: MouseEvent<HTMLButtonElement>) => void;
  className?: string;
  dir?: string;
}

/** Compact label for statuses, tags and versions. */
export function Chip({ label, color = 'default', variant = 'filled', size = 'small', onDelete, className, dir }: ChipProps) {
  return (
    <span
      dir={dir}
      className={cx(
        'inline-flex max-w-full items-center gap-1 rounded-md font-medium',
        size === 'small' ? 'h-6 px-2 text-xs' : 'h-7 px-2.5 text-sm',
        variant === 'outlined' ? outlinedClasses[color] : filledClasses[color],
        className,
      )}
    >
      <span className="truncate">{label}</span>
      {onDelete && (
        <button
          type="button"
          aria-label="حذف"
          onClick={onDelete}
          className="-me-1 inline-flex size-4 items-center justify-center rounded-full text-paper-400 hover:bg-black/10 hover:text-ink-800"
        >
          <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      )}
    </span>
  );
}
