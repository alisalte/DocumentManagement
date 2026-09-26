import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import { cx } from './cx';
import { Spinner } from './Spinner';

export interface ComboboxOption {
  id: string;
  label: string;
}

export interface ComboboxProps {
  label?: ReactNode;
  value: ComboboxOption | null;
  onChange: (option: ComboboxOption | null) => void;
  options: ComboboxOption[];
  /** Feed new options as the user types (server-side search). */
  onInputChange?: (text: string) => void;
  loading?: boolean;
  required?: boolean;
  error?: boolean;
  helperText?: ReactNode;
  placeholder?: string;
  disabled?: boolean;
  emptyText?: string;
  className?: string;
}

/** Search-as-you-type single picker for people, groups and documents. */
export function Combobox({
  label,
  value,
  onChange,
  options,
  onInputChange,
  loading,
  required,
  error,
  helperText,
  placeholder,
  disabled,
  emptyText = 'موردی پیدا نشد.',
  className,
}: ComboboxProps) {
  const listId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [text, setText] = useState(value?.label ?? '');
  const [active, setActive] = useState(-1);

  useEffect(() => {
    if (!open) setText(value?.label ?? '');
  }, [value, open]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (rootRef.current?.contains(event.target as Node)) return;
      setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  const commit = (option: ComboboxOption | null) => {
    onChange(option);
    setOpen(false);
    setText(option?.label ?? '');
    setActive(-1);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setOpen(true);
      setActive((current) => Math.min(current + 1, options.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((current) => Math.max(current - 1, 0));
    } else if (event.key === 'Enter' && open && active >= 0 && options[active]) {
      event.preventDefault();
      commit(options[active]);
    } else if (event.key === 'Escape') {
      setOpen(false);
    }
  };

  const showList = open && !disabled;

  return (
    <div ref={rootRef} className={cx('relative w-full', className)}>
      {label !== undefined && (
        <label htmlFor={listId} className="mb-1.5 block text-sm font-medium text-ink-800">
          {label}
          {required && <span className="text-rose-500"> *</span>}
        </label>
      )}
      <div className="relative">
        <input
          id={listId}
          ref={inputRef}
          role="combobox"
          aria-expanded={showList}
          aria-controls={listId}
          aria-autocomplete="list"
          autoComplete="off"
          disabled={disabled}
          required={required}
          placeholder={placeholder}
          value={text}
          onFocus={() => !disabled && setOpen(true)}
          onChange={(event) => {
            setText(event.target.value);
            setActive(-1);
            setOpen(true);
            onInputChange?.(event.target.value);
          }}
          onKeyDown={onKeyDown}
          className={cx(
            'h-10 w-full rounded-xl border bg-white pe-9 ps-3.5 text-sm text-ink-900 shadow-sm transition-colors',
            'placeholder:text-paper-400 focus:outline-none focus:ring-2 focus:ring-ink-500/20',
            'disabled:cursor-not-allowed disabled:bg-paper-100 disabled:text-paper-500',
            error ? 'border-rose-400 focus:border-rose-500 focus:ring-rose-500/20' : 'border-paper-300 focus:border-ink-500',
          )}
        />
        <span className="absolute inset-y-0 end-3 flex items-center">
          {loading ? <Spinner size="sm" /> : <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-4 text-paper-400"><path d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z" /></svg>}
        </span>
        {value && !disabled && (
          <button
            type="button"
            aria-label="پاک کردن"
            onClick={() => commit(null)}
            className="absolute inset-y-0 start-2 my-auto size-5 rounded-md text-paper-400 hover:bg-ink-50 hover:text-paper-600"
          >
            <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="mx-auto size-3.5">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
            </svg>
          </button>
        )}
      </div>
      {showList && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-40 mt-1 max-h-60 w-full overflow-y-auto rounded-xl border border-paper-200 bg-white py-1 shadow-xl"
        >
          {options.length === 0 && !loading && <li className="px-3 py-2 text-sm text-paper-400">{emptyText}</li>}
          {options.map((option, index) => (
            <li key={option.id} role="option" aria-selected={value?.id === option.id}>
              <button
                type="button"
                tabIndex={-1}
                onMouseEnter={() => setActive(index)}
                onClick={() => commit(option)}
                className={cx(
                  'flex w-full items-center px-3 py-2 text-start text-sm',
                  index === active ? 'bg-ink-50 text-ink-800' : 'text-ink-800',
                )}
              >
                <span className="truncate">{option.label}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {helperText != null && <p className={cx('mt-1.5 text-xs', error ? 'text-rose-600' : 'text-paper-500')}>{helperText}</p>}
    </div>
  );
}
