import type { ButtonHTMLAttributes, ElementType, ReactNode } from 'react';
import { cx } from './cx';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const variantClasses: Record<ButtonVariant, string> = {
  primary: 'bg-brand-600 text-white shadow-sm hover:bg-brand-700 active:bg-brand-800',
  secondary: 'bg-accent-600 text-white shadow-sm hover:bg-accent-700 active:bg-accent-800',
  outline: 'border border-slate-300 bg-white text-slate-700 shadow-sm hover:bg-slate-50 active:bg-slate-100',
  ghost: 'text-slate-600 hover:bg-slate-100 hover:text-slate-900',
  danger: 'bg-rose-600 text-white shadow-sm hover:bg-rose-700 active:bg-rose-800',
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'h-8 gap-1.5 px-3 text-xs',
  md: 'h-10 gap-2 px-4 text-sm',
  lg: 'h-11 gap-2.5 px-6 text-sm',
};

/** Class list for a button, also usable on links styled as buttons. */
export function buttonClasses(
  options: { variant?: ButtonVariant; size?: ButtonSize; fullWidth?: boolean; className?: string } = {},
): string {
  const { variant = 'primary', size = 'md', fullWidth, className } = options;
  return cx(
    'inline-flex select-none items-center justify-center rounded-lg font-medium transition-colors',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600',
    'disabled:pointer-events-none disabled:opacity-50',
    variantClasses[variant],
    sizeClasses[size],
    fullWidth && 'w-full',
    className,
  );
}

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and blocks clicks. */
  loading?: boolean;
  fullWidth?: boolean;
  /** Renders as a router link when given `to`, e.g. `as={RouterLink} to="/new"`. */
  as?: ElementType;
  to?: string;
  children?: ReactNode;
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  fullWidth,
  as,
  to,
  className,
  children,
  disabled,
  type,
  onClick,
  ...rest
}: ButtonProps) {
  const Component = (as ?? 'button') as ElementType;
  const blocked = !!disabled || loading;

  if (as) {
    return (
      <Component
        {...rest}
        to={to}
        aria-disabled={blocked || undefined}
        className={buttonClasses({ variant, size, fullWidth, className })}
        onClick={blocked ? undefined : onClick}
      >
        {loading && <Spinner size="sm" />}
        {children}
      </Component>
    );
  }

  return (
    <Component
      {...rest}
      type={type ?? 'button'}
      disabled={blocked}
      className={buttonClasses({ variant, size, fullWidth, className })}
      onClick={onClick}
    >
      {loading && <Spinner size="sm" />}
      {children}
    </Component>
  );
}

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  /** Required: the icon is decorative, the label is the accessible name. */
  label: string;
  variant?: 'ghost' | 'outline';
  size?: 'sm' | 'md';
  children: ReactNode;
}

/** Square icon-only button. */
export function IconButton({ label, variant = 'ghost', size = 'md', className, children, type, ...rest }: IconButtonProps) {
  return (
    <button
      {...rest}
      type={type ?? 'button'}
      aria-label={label}
      title={label}
      className={cx(
        'inline-flex items-center justify-center rounded-lg transition-colors',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600',
        'disabled:pointer-events-none disabled:opacity-50',
        variant === 'outline' ? 'border border-slate-300 bg-white text-slate-600 hover:bg-slate-50' : 'text-slate-500 hover:bg-slate-100 hover:text-slate-800',
        size === 'sm' ? 'size-7' : 'size-9',
        className,
      )}
    >
      {children}
    </button>
  );
}
