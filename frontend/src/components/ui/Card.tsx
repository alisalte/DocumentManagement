import type { HTMLAttributes } from 'react';
import { cx } from './cx';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  /** Removes the outer padding when the card holds a table or a list. */
  flush?: boolean;
}

/** The standard surface: page sections, panels and forms sit on a card. */
export function Card({ flush, className, children, ...rest }: CardProps) {
  return (
    <div {...rest} className={cx('rounded-xl border border-slate-200 bg-white shadow-sm', !flush && 'p-4 sm:p-5', className)}>
      {children}
    </div>
  );
}
