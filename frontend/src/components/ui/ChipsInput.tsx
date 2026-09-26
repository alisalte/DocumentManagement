import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import { cx } from './cx';
import { Chip } from './Chip';
import { Spinner } from './Spinner';

export interface ChipsInputProps {
  label?: ReactNode;
  value: string[];
  onChange: (value: string[]) => void;
  /** Suggested values shown while typing; picking one adds it. */
  suggestions?: string[];
  onInputChange?: (text: string) => void;
  loading?: boolean;
  helperText?: ReactNode;
  error?: boolean;
  disabled?: boolean;
  placeholder?: string;
  required?: boolean;
  className?: string;
}

/** Free-text chips: Enter (or comma) adds, Backspace on empty removes. */
export function ChipsInput({
  label,
  value,
  onChange,
  suggestions = [],
  onInputChange,
  loading,
  helperText,
  error,
  disabled,
  placeholder,
  required,
  className,
}: ChipsInputProps) {
  const inputId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (rootRef.current?.contains(event.target as Node)) return;
      setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  const add = (raw: string) => {
    const next = raw.trim();
    if (!next) return;
    if (!value.includes(next)) onChange([...value, next]);
    setText('');
    onInputChange?.('');
  };

  const remove = (tag: string) => onChange(value.filter((item) => item !== tag));

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      add(text);
    } else if (event.key === 'Backspace' && text === '' && value.length > 0) {
      remove(value[value.length - 1]);
    } else if (event.key === 'Escape') {
      setOpen(false);
    }
  };

  const visible = open && !disabled && (loading || suggestions.length > 0 || text.trim().length > 0);

  return (
    <div ref={rootRef} className={cx('relative w-full', className)}>
      {label !== undefined && (
        <label htmlFor={inputId} className="mb-1.5 block text-sm font-medium text-ink-800">
          {label}
          {required && <span className="text-rose-500"> *</span>}
        </label>
      )}
      <div
        onClick={() => inputRef.current?.focus()}
        className={cx(
          'flex min-h-10 w-full cursor-text flex-wrap items-center gap-1.5 rounded-lg border bg-white px-2 py-1.5 shadow-sm transition-colors',
          'focus-within:ring-2 focus-within:ring-ink-500/20',
          error ? 'border-rose-400 focus-within:border-rose-500' : 'border-paper-300 focus-within:border-ink-500',
          disabled && 'cursor-not-allowed bg-paper-100 opacity-60',
        )}
      >
        {value.map((tag) => (
          <Chip key={tag} label={tag} size="small" onDelete={disabled ? undefined : () => remove(tag)} />
        ))}
        <input
          id={inputId}
          ref={inputRef}
          type="text"
          disabled={disabled}
          placeholder={value.length === 0 ? placeholder : undefined}
          value={text}
          onChange={(event) => {
            setText(event.target.value);
            setOpen(true);
            onInputChange?.(event.target.value);
          }}
          onKeyDown={onKeyDown}
          onFocus={() => setOpen(true)}
          className="h-6 min-w-24 flex-1 bg-transparent text-sm text-ink-900 placeholder:text-paper-400 focus:outline-none disabled:cursor-not-allowed"
        />
        {loading && <Spinner size="sm" />}
      </div>
      {visible && (
        <ul className="absolute z-40 mt-1 max-h-48 w-full overflow-y-auto rounded-xl border border-paper-200 bg-white py-1 shadow-xl">
          {loading && !suggestions.length && <li className="px-3 py-2 text-sm text-paper-400">در حال جستجو…</li>}
          {suggestions
            .filter((item) => !value.includes(item) && item.includes(text.trim()))
            .map((item) => (
              <li key={item}>
                <button
                  type="button"
                  tabIndex={-1}
                  onClick={() => {
                    add(item);
                    setOpen(false);
                  }}
                  className="w-full px-3 py-2 text-start text-sm text-ink-800 hover:bg-ink-50 hover:text-ink-800"
                >
                  {item}
                </button>
              </li>
            ))}
        </ul>
      )}
      {helperText != null && <p className={cx('mt-1.5 text-xs', error ? 'text-rose-600' : 'text-paper-500')}>{helperText}</p>}
    </div>
  );
}
