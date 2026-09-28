import type { ButtonHTMLAttributes, ElementType, ReactNode } from 'react';
import { cx } from './cx';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const variantClasses: Record<ButtonVariant, string> = {
  primary:
    'bg-ink-700 text-white shadow-[0_1px_2px_rgb(12_32_52/0.12)] hover:bg-ink-800 active:bg-ink-900',
  secondary:
    'bg-copper-600 text-white shadow-[0_1px_2px_rgb(58_38_26/0.12)] hover:bg-copper-700 active:bg-copper-800',
  outline:
    'border border-paper-300 bg-white/90 text-ink-800 shadow-sm hover:border-ink-300 hover:bg-ink-50 active:bg-ink-100',
  ghost: 'text-paper-700 hover:bg-ink-50 hover:text-ink-900',
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
    'inline-flex select-none items-center justify-center rounded-xl font-medium transition-all duration-150',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-600',
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
        'inline-flex items-center justify-center rounded-xl transition-colors duration-150',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-600',
        'disabled:pointer-events-none disabled:opacity-50',
        variant === 'outline'
          ? 'border border-paper-300 bg-white/90 text-paper-700 hover:border-ink-300 hover:bg-ink-50'
          : 'text-paper-600 hover:bg-ink-50 hover:text-ink-800',
        size === 'sm' ? 'size-7' : 'size-9',
        className,
      )}
    >
      {children}
    </button>
  );
}
