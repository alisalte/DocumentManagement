import type { HTMLAttributes, TdHTMLAttributes, ThHTMLAttributes } from 'react';
import { cx } from './cx';

export interface TableProps extends HTMLAttributes<HTMLTableElement> {
  /** Tighter rows for dense admin tables. */
  dense?: boolean;
}

/** Responsive table; scrolls sideways on narrow screens. */
export function Table({ dense, className, children, ...rest }: TableProps) {
  return (
    <div className="w-full overflow-x-auto">
      <table {...rest} className={cx('w-full border-collapse text-sm', dense ? 'text-xs' : 'text-sm', className)}>
        {children}
      </table>
    </div>
  );
}

export function THead({ children, className }: { children?: React.ReactNode; className?: string }) {
  return <thead className={cx('bg-slate-50 text-slate-600', className)}>{children}</thead>;
}

export function TBody({ children, className }: { children?: React.ReactNode; className?: string }) {
  return <tbody className={cx('divide-y divide-slate-100', className)}>{children}</tbody>;
}

export function TR({ children, className, ...rest }: HTMLAttributes<HTMLTableRowElement>) {
  return (
    <tr {...rest} className={cx('transition-colors hover:bg-slate-50/70', className)}>
      {children}
    </tr>
  );
}

export function TH({ children, className, ...rest }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      {...rest}
      scope={rest.scope ?? 'col'}
      className={cx('border-b border-slate-200 px-4 py-3 text-start font-semibold whitespace-nowrap', className)}
    >
      {children}
    </th>
  );
}

export function TD({ children, className, ...rest }: TdHTMLAttributes<HTMLTableCellElement>) {
  return (
    <td {...rest} className={cx('px-4 py-3 align-middle text-slate-700', className)}>
      {children}
    </td>
  );
}
