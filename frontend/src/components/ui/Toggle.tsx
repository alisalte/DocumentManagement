import type { InputHTMLAttributes, ReactNode } from 'react';
import { cx } from './cx';

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'size'> {
  /** Pass a label to wrap the box; omit inside a labelled row. */
  label?: ReactNode;
}

/** Square multi-select control. */
export function Checkbox({ label, className, ...rest }: CheckboxProps) {
  const box = (
    <input
      {...rest}
      type="checkbox"
      className={cx(
        'size-4 shrink-0 cursor-pointer rounded border-slate-300 accent-brand-600',
        'focus:outline-none focus:ring-2 focus:ring-brand-500/30 focus:ring-offset-1',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
    />
  );
  if (label === undefined) return box;
  return (
    <label className={cx('inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700', rest.disabled && 'opacity-60')}>
      {box}
      <span>{label}</span>
    </label>
  );
}

export interface SwitchProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'size'> {
  label?: ReactNode;
}

/** On/off toggle. Keeps the native input so `onChange` reads `event.target.checked`. */
export function Switch({ label, className, ...rest }: SwitchProps) {
  return (
    <label className={cx('inline-flex cursor-pointer items-center gap-2.5 text-sm text-slate-700', rest.disabled && 'opacity-60')}>
      <input {...rest} type="checkbox" role="switch" className="peer sr-only" />
      <span
        aria-hidden
        className={cx(
          'relative h-5 w-9 shrink-0 rounded-full bg-slate-300 transition-colors',
          'peer-checked:bg-brand-600 peer-focus-visible:ring-2 peer-focus-visible:ring-brand-500/40 peer-focus-visible:ring-offset-2',
          'after:absolute after:top-0.5 after:size-4 after:rounded-full after:bg-white after:shadow after:transition-all after:start-0.5',
          'peer-checked:after:start-[1.125rem]',
          'peer-disabled:cursor-not-allowed peer-disabled:opacity-50',
          className,
        )}
      />
      {label !== undefined && <span>{label}</span>}
    </label>
  );
}
