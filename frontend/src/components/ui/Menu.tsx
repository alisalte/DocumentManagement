import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { cx } from './cx';

export interface MenuProps {
  /** The button the menu is attached to; `null` closes it. Ignored when `point` is set. */
  anchor: HTMLElement | null;
  /** Viewport point for a context menu opened at the pointer. */
  point?: { x: number; y: number } | null;
  onClose: () => void;
  children: ReactNode;
  className?: string;
}

interface Position {
  top: number;
  left: number;
}

/** Anchored dropdown, or a context menu at `point`. Rendered in a portal so overflow never clips it. */
export function Menu({ anchor, point = null, onClose, children, className }: MenuProps) {
  const open = point !== null || anchor !== null;
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState<Position | null>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node;
      if (ref.current?.contains(target) || anchor?.contains(target)) return;
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
      const width = element.offsetWidth;
      const height = element.offsetHeight;
      const clampLeft = (rawLeft: number) =>
        Math.min(Math.max(8, rawLeft), Math.max(8, window.innerWidth - width - 8));

      const commit = (top: number, left: number) => {
        setPosition((current) => (current && current.top === top && current.left === left ? current : { top, left }));
      };

      if (point) {
        const top = point.y + height > window.innerHeight - 8 ? Math.max(8, point.y - height) : point.y;
        commit(top, clampLeft(point.x));
        return;
      }

      if (!anchor) return;
      const rect = anchor.getBoundingClientRect();
      const rtl = document.documentElement.dir === 'rtl';
      const rawLeft = rtl ? rect.right - width : rect.left;
      const rawTop = rect.bottom + 6;
      const top = rawTop + height > window.innerHeight - 8 ? Math.max(8, rect.top - height - 6) : rawTop;
      commit(top, clampLeft(rawLeft));
    };
    place();
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    return () => {
      window.removeEventListener('resize', place);
      window.removeEventListener('scroll', place, true);
    };
  }, [open, anchor, point]);

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
        zIndex: 70,
      }}
      className={cx(
        'z-50 min-w-44 overflow-hidden rounded-xl border border-paper-200 bg-paper-50 py-1',
        'shadow-[0_4px_12px_rgb(31_22_56/0.08),0_16px_40px_rgb(31_22_56/0.1)]',
        className,
      )}
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
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
        'disabled:cursor-not-allowed disabled:opacity-50',
        danger ? 'text-rose-600 hover:bg-rose-50' : 'text-ink-800 hover:bg-ink-50',
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
    'flex w-full items-center gap-2 px-3 py-2 text-start text-sm text-ink-800 transition-colors hover:bg-ink-50',
    'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
    className,
  );
}
