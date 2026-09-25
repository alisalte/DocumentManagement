import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { cx } from './cx';

export interface MenuProps {
  /** The button the menu is attached to; `null` closes it. */
  anchor: HTMLElement | null;
  onClose: () => void;
  children: ReactNode;
  className?: string;
}

interface Position {
  top: number;
  left: number;
}

/** Anchored dropdown, rendered in a portal so overflow never clips it. */
export function Menu({ anchor, onClose, children, className }: MenuProps) {
  const open = anchor !== null;
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState<Position | null>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node;
      if (ref.current?.contains(target) || anchor.contains(target)) return;
      onClose();
    };
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open, anchor, onClose]);

  useLayoutEffect(() => {
    if (!open) {
      setPosition(null);
      return;
    }
    const place = () => {
      const element = ref.current;
      if (!element) return;
      const rect = anchor.getBoundingClientRect();
      const width = element.offsetWidth;
      const height = element.offsetHeight;
      const rtl = document.documentElement.dir === 'rtl';
      const rawLeft = rtl ? rect.right - width : rect.left;
      const left = Math.min(Math.max(8, rawLeft), Math.max(8, window.innerWidth - width - 8));
      const rawTop = rect.bottom + 6;
      const top = rawTop + height > window.innerHeight - 8 ? Math.max(8, rect.top - height - 6) : rawTop;
      setPosition({ top, left });
    };
    place();
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    return () => {
      window.removeEventListener('resize', place);
      window.removeEventListener('scroll', place, true);
    };
  }, [open, anchor]);

  if (!open) return null;

  return createPortal(
    <div
      ref={ref}
      role="menu"
      style={{
        position: 'fixed',
        top: position?.top ?? -10000,
        left: position?.left ?? -10000,
        visibility: position ? 'visible' : 'hidden',
      }}
      className={cx('z-50 min-w-44 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-xl', className)}
    >
      {children}
    </div>,
    document.body,
  );
}

export interface MenuItemProps {
  onClick?: () => void;
  disabled?: boolean;
  /** Renders a danger row, for destructive actions. */
  danger?: boolean;
  children?: ReactNode;
  className?: string;
}

/** One row of a menu. */
export function MenuItem({ onClick, disabled, danger, children, className }: MenuItemProps) {
  return (
    <button
      type="button"
      role="menuitem"
      disabled={disabled}
      onClick={onClick}
      className={cx(
        'flex w-full items-center gap-2 px-3 py-2 text-start text-sm transition-colors',
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-brand-600',
        'disabled:cursor-not-allowed disabled:opacity-50',
        danger ? 'text-rose-600 hover:bg-rose-50' : 'text-slate-700 hover:bg-slate-100',
        className,
      )}
    >
      {children}
    </button>
  );
}

/** Class list for a row that is really a link (use on `RouterLink`). */
export function menuItemClasses(className?: string): string {
  return cx(
    'flex w-full items-center gap-2 px-3 py-2 text-start text-sm text-slate-700 transition-colors hover:bg-slate-100',
    'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-brand-600',
    className,
  );
}
