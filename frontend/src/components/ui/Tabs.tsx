import { createContext, useContext } from 'react';
import type { ReactNode } from 'react';
import { cx } from './cx';

interface TabsContextValue {
  value: string;
  onChange: (value: string) => void;
}

const TabsContext = createContext<TabsContextValue | null>(null);

export interface TabsProps {
  value: string;
  onChange: (value: string) => void;
  /** Tabs fill the row instead of hugging their labels. */
  variant?: 'default' | 'fullWidth';
  children?: ReactNode;
  className?: string;
}

/** Row of tab buttons with an underline on the active one. */
export function Tabs({ value, onChange, variant = 'default', children, className }: TabsProps) {
  return (
    <TabsContext.Provider value={{ value, onChange }}>
      <div role="tablist" className={cx('flex gap-1 border-b border-paper-200', variant === 'fullWidth' && 'w-full', className)}>
        {children}
      </div>
    </TabsContext.Provider>
  );
}

export interface TabProps {
  value: string;
  label: ReactNode;
  disabled?: boolean;
}

/** One tab; must sit inside `Tabs`. */
export function Tab({ value, label, disabled }: TabProps) {
  const context = useContext(TabsContext);
  const active = context?.value === value;
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      disabled={disabled}
      onClick={() => context?.onChange(value)}
      className={cx(
        'relative -mb-px border-b-2 px-4 py-2.5 text-sm font-medium transition-colors duration-150',
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
        'disabled:cursor-not-allowed disabled:opacity-50',
        active ? 'border-ink-700 text-ink-800' : 'border-transparent text-paper-500 hover:border-paper-300 hover:text-ink-800',
      )}
    >
      {label}
    </button>
  );
}
