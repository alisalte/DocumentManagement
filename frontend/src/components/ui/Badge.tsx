import type { ReactNode } from 'react';
import { cx } from './cx';

export interface BadgeProps {
  /** Hidden when zero. */
  count?: number;
  max?: number;
  children?: ReactNode;
  className?: string;
}

/** Small count bubble over an icon, for notifications and task inboxes. */
export function Badge({ count = 0, max = 99, children, className }: BadgeProps) {
  const shown = count > max ? `${max}+` : String(count);
  return (
    <span className={cx('relative inline-flex', className)}>
      {children}
      {count > 0 && (
        <span
          className={cx(
            'absolute top-0 end-0 min-w-4 -translate-y-1/2 rounded-full bg-accent-600 px-1 text-center text-[10px] leading-4 font-semibold text-white',
            'ltr:translate-x-1/2 rtl:-translate-x-1/2',
          )}
        >
          {shown}
        </span>
      )}
    </span>
  );
}
