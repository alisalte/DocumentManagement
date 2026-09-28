import type { HTMLAttributes } from 'react';
import { cx } from './cx';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  /** Removes the outer padding when the card holds a table or a list. */
  flush?: boolean;
}

/** The standard surface: page sections, panels and forms sit on a card. */
export function Card({ flush, className, children, ...rest }: CardProps) {
  return (
    <div
      {...rest}
      className={cx(
        'rounded-2xl border border-paper-200/90 bg-white/90 backdrop-blur-sm',
        'shadow-[0_1px_2px_rgb(12_32_52/0.04),0_8px_24px_rgb(12_32_52/0.06)]',
        !flush && 'p-4 sm:p-5',
        className,
      )}
    >
      {children}
    </div>
  );
}
