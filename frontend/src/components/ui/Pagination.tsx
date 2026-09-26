import { cx } from './cx';

export interface PaginationProps {
  count: number;
  page: number;
  onChange: (page: number) => void;
  className?: string;
}

const fa = (n: number) => n.toLocaleString('fa-IR');

/** Windowed page numbers with previous/next. */
export function Pagination({ count, page, onChange, className }: PaginationProps) {
  if (count <= 1) return null;

  const pages: Array<number | 'gap'> = [];
  for (let index = 1; index <= count; index += 1) {
    const qualifies = index === 1 || index === count || Math.abs(index - page) <= 1;
    const previous = pages[pages.length - 1];
    if (qualifies) {
      if (previous === 'gap') pages.pop();
      pages.push(index);
    } else if (previous !== 'gap') {
      pages.push('gap');
    }
  }

  const itemClasses = (options: { active?: boolean; disabled?: boolean } = {}) =>
    cx(
      'inline-flex h-9 min-w-9 items-center justify-center rounded-xl px-2 text-sm transition-colors duration-150',
      'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-600',
      options.disabled ? 'cursor-not-allowed text-paper-300' : 'text-paper-600 hover:bg-ink-50',
      options.active && !options.disabled && 'bg-ink-700 font-semibold text-white hover:bg-ink-800',
    );

  return (
    <nav aria-label="صفحه‌بندی" className={cx('flex flex-wrap items-center justify-center gap-1', className)}>
      <button type="button" disabled={page <= 1} onClick={() => onChange(page - 1)} className={itemClasses({ disabled: page <= 1 })}>
        قبلی
      </button>
      {pages.map((entry, index) =>
        entry === 'gap' ? (
          <span key={`gap-${index}`} className="px-1 text-paper-400">
            …
          </span>
        ) : (
          <button
            key={entry}
            type="button"
            aria-current={entry === page ? 'page' : undefined}
            onClick={() => onChange(entry)}
            className={itemClasses({ active: entry === page })}
          >
            {fa(entry)}
          </button>
        ),
      )}
      <button
        type="button"
        disabled={page >= count}
        onClick={() => onChange(page + 1)}
        className={itemClasses({ disabled: page >= count })}
      >
        بعدی
      </button>
    </nav>
  );
}
