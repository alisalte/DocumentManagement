import type { ReactNode } from 'react';
import { cx } from './cx';

export interface TooltipProps {
  /** The bubble text. */
  label: ReactNode;
  children: ReactNode;
  className?: string;
}

/** Hint shown while hovering or focusing the child. */
export function Tooltip({ label, children, className }: TooltipProps) {
  return (
    <span className={cx('group/tooltip relative inline-flex', className)}>
      {children}
      <span
        role="tooltip"
        className="pointer-events-none absolute bottom-full left-1/2 z-50 mb-1.5 -translate-x-1/2 rounded-md bg-slate-900 px-2 py-1 text-xs whitespace-nowrap text-white opacity-0 shadow-md transition-opacity group-hover/tooltip:opacity-100 group-focus-within/tooltip:opacity-100"
      >
        {label}
      </span>
    </span>
  );
}
