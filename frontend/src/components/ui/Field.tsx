import { useId } from 'react';
import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react';
import { cx } from './cx';

const controlBase =
  'w-full rounded-lg border bg-white text-sm text-slate-900 shadow-sm transition-colors placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-brand-500/20 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:text-slate-500';
const borderDefault = 'border-slate-300 focus:border-brand-500';
const borderError = 'border-rose-400 focus:border-rose-500 focus:ring-rose-500/20';
const sizes = { sm: 'h-9 px-3', md: 'h-10 px-3.5' } as const;

interface FieldChrome {
  label?: ReactNode;
  helperText?: ReactNode;
  /** Renders the message in the error tone and outlines the control. */
  error?: boolean;
  required?: boolean;
}

interface ShellProps extends FieldChrome {
  id: string;
  control: ReactNode;
  className?: string;
}

function Shell({ id, label, helperText, error, required, control, className }: ShellProps) {
  return (
    <div className={cx('w-full', className)}>
      {label !== undefined && (
        <label htmlFor={id} className="mb-1.5 block text-sm font-medium text-slate-700">
          {label}
          {required && <span className="text-rose-500"> *</span>}
        </label>
      )}
      {control}
      {helperText != null && (
        <p className={cx('mt-1.5 text-xs', error ? 'text-rose-600' : 'text-slate-500')}>{helperText}</p>
      )}
    </div>
  );
}

export interface TextFieldProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size' | 'children'>,
    FieldChrome {
  size?: 'sm' | 'md';
  /** Icon or spinner shown inside the control on the trailing edge. */
  endAdornment?: ReactNode;
}

/** Single-line input with an optional label and message. */
export function TextField({
  label,
  helperText,
  error,
  required,
  size = 'md',
  endAdornment,
  className,
  id,
  ...rest
}: TextFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Shell
      id={fieldId}
      label={label}
      helperText={helperText}
      error={error}
      required={required}
      className={className}
      control={
        <div className="relative">
          <input
            {...rest}
            id={fieldId}
            required={required}
            aria-invalid={error || undefined}
            className={cx(
              controlBase,
              sizes[size],
              error ? borderError : borderDefault,
              endAdornment ? 'pe-9' : null,
            )}
          />
          {endAdornment && (
            <span className="pointer-events-none absolute inset-y-0 end-3 flex items-center text-slate-400">
              {endAdornment}
            </span>
          )}
        </div>
      }
    />
  );
}

export interface TextAreaProps
  extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'children'>,
    FieldChrome {
  className?: string;
}

/** Multi-line input. */
export function TextArea({ label, helperText, error, required, className, id, rows = 3, ...rest }: TextAreaProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Shell
      id={fieldId}
      label={label}
      helperText={helperText}
      error={error}
      required={required}
      className={className}
      control={
        <textarea
          {...rest}
          id={fieldId}
          rows={rows}
          required={required}
          aria-invalid={error || undefined}
          className={cx(
            controlBase,
            'min-h-20 resize-y px-3.5 py-2 leading-6',
            error ? borderError : borderDefault,
          )}
        />
      }
    />
  );
}

export interface SelectProps
  extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'size' | 'children'>,
    FieldChrome {
  size?: 'sm' | 'md';
  children: ReactNode;
}

/** Native select styled like the other controls; use `<option>` children. */
export function Select({
  label,
  helperText,
  error,
  required,
  size = 'md',
  className,
  id,
  children,
  ...rest
}: SelectProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  return (
    <Shell
      id={fieldId}
      label={label}
      helperText={helperText}
      error={error}
      required={required}
      className={className}
      control={
        <div className="relative">
          <select
            {...rest}
            id={fieldId}
            required={required}
            aria-invalid={error || undefined}
            className={cx(
              controlBase,
              sizes[size],
              'appearance-none pe-9',
              error ? borderError : borderDefault,
            )}
          >
            {children}
          </select>
          <svg
            viewBox="0 0 20 20"
            fill="currentColor"
            aria-hidden
            className="pointer-events-none absolute inset-y-0 end-3 my-auto size-4 text-slate-400"
          >
            <path
              fillRule="evenodd"
              d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z"
              clipRule="evenodd"
            />
          </svg>
        </div>
      }
    />
  );
}
