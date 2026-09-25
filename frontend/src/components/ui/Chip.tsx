import type { MouseEvent, ReactNode } from 'react';
import { cx } from './cx';

export type ChipColor = 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'error';

const filledClasses: Record<ChipColor, string> = {
  default: 'bg-slate-100 text-slate-700',
  primary: 'bg-brand-100 text-brand-800',
  secondary: 'bg-accent-100 text-accent-800',
  success: 'bg-emerald-100 text-emerald-800',
  warning: 'bg-amber-100 text-amber-800',
  error: 'bg-rose-100 text-rose-800',
};

const outlinedClasses: Record<ChipColor, string> = {
  default: 'border border-slate-300 bg-white text-slate-600',
  primary: 'border border-brand-200 bg-white text-brand-700',
  secondary: 'border border-accent-200 bg-white text-accent-700',
  success: 'border border-emerald-200 bg-white text-emerald-700',
  warning: 'border border-amber-200 bg-white text-amber-700',
  error: 'border border-rose-200 bg-white text-rose-700',
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
        'inline-flex max-w-full items-center gap-1 rounded-full font-medium',
        size === 'small' ? 'h-6 px-2.5 text-xs' : 'h-7 px-3 text-sm',
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
          className="-me-1 inline-flex size-4 items-center justify-center rounded-full text-slate-400 hover:bg-black/10 hover:text-slate-700"
        >
          <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      )}
    </span>
  );
}
