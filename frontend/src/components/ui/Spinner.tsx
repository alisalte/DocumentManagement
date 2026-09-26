import { cx } from './cx';

export interface SpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

/** Indeterminate activity indicator. */
export function Spinner({ size = 'md', className }: SpinnerProps) {
  const box = size === 'sm' ? 'size-4 border-2' : size === 'lg' ? 'size-10 border-4' : 'size-6 border-2';
  return (
    <span
      role="status"
      aria-label="در حال بارگذاری"
      className={cx('inline-block animate-spin rounded-full border-paper-300 border-t-ink-700', box, className)}
    />
  );
}

export interface ProgressBarProps {
  className?: string;
}

/** Thin indeterminate bar, shown while a list or page refreshes. */
export function ProgressBar({ className }: ProgressBarProps) {
  return (
    <div
      role="progressbar"
      aria-label="در حال بارگذاری"
      className={cx('h-1 w-full overflow-hidden rounded-full bg-paper-200', className)}
    >
      <div className="h-full w-1/3 animate-progress rounded-full bg-ink-600" />
    </div>
  );
}

export interface CenteredSpinnerProps {
  size?: SpinnerProps['size'];
}

/** Full-height loading state for a page or panel. */
export function CenteredSpinner({ size = 'md' }: CenteredSpinnerProps) {
  return (
    <div className="grid place-items-center py-16">
      <Spinner size={size} />
    </div>
  );
}
