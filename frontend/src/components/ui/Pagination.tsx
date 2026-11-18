import { cx } from './cx';

export interface PaginationProps {
  /** Number of pages. */
  count: number;
  /** Current page, starting at 1. */
  page: number;
  onChange: (page: number) => void;
  /** With `pageSize`, shows «نمایش ۲۶ تا ۵۰ از ۱۳۷» next to the buttons. */
  total?: number;
  pageSize?: number;
  /** Disables the buttons while the next page loads. */
  disabled?: boolean;
  className?: string;
}

const fa = (n: number) => n.toLocaleString('fa-IR');

/** The page numbers to show: always the first and last, the current one and its neighbours. */
export function pageWindow(count: number, page: number, siblings = 1): Array<number | 'gap'> {
  const pages: Array<number | 'gap'> = [];
  for (let index = 1; index <= count; index += 1) {
    const qualifies = index === 1 || index === count || Math.abs(index - page) <= siblings;
    const previous = pages[pages.length - 1];
    if (qualifies) {
      // A gap that hides a single page is longer than the page itself.
      if (previous === 'gap' && pages[pages.length - 2] === index - 2) {
        pages.pop();
        pages.push(index - 1);
      }
      pages.push(index);
    } else if (previous !== 'gap') {
      pages.push('gap');
    }
  }
  return pages;
}

function Chevron({ direction }: { direction: 'previous' | 'next' }) {
  // The app is right to left: «previous» points right, «next» points left. (Direction variants
  // are unreliable inside buttons, so this is fixed rather than `rtl:`-dependent.)
  return (
    <svg
      viewBox="0 0 20 20"
      fill="currentColor"
      aria-hidden
      className={cx('size-4 shrink-0', direction === 'next' && 'rotate-180')}
    >
      <path
        fillRule="evenodd"
        d="M8.22 5.22a.75.75 0 0 1 1.06 0l4.25 4.25a.75.75 0 0 1 0 1.06l-4.25 4.25a.75.75 0 0 1-1.06-1.06L11.94 10 8.22 6.28a.75.75 0 0 1 0-1.06Z"
        clipRule="evenodd"
      />
    </svg>
  );
}

/** Page numbers with previous/next, a «showing x–y of z» summary and a compact phone layout. */
export function Pagination({ count, page, onChange, total, pageSize, disabled, className }: PaginationProps) {
  if (count <= 1 && total === undefined) return null;

  const current = Math.min(Math.max(1, page), Math.max(1, count));
  const go = (target: number) => {
    const next = Math.min(Math.max(1, target), count);
    if (next !== current) onChange(next);
  };

  const item = (options: { active?: boolean; disabled?: boolean } = {}) =>
    cx(
      'inline-flex h-9 min-w-9 items-center justify-center gap-1 rounded-lg px-2.5 text-sm font-medium tabular-nums transition-colors duration-150',
      'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-500',
      options.active
        ? 'bg-ink-600 text-white shadow-[var(--shadow-brand)]'
        : options.disabled
          ? 'cursor-not-allowed text-paper-400'
          : 'text-paper-600 hover:bg-paper-200/70 hover:text-ink-900',
    );

  const summary =
    total !== undefined && pageSize ? (
      total === 0 ? (
        <p className="text-xs text-paper-500">موردی برای نمایش نیست</p>
      ) : (
        <p className="text-xs text-paper-500">
          نمایش{' '}
          <span className="font-semibold text-ink-900 tabular-nums">{fa((current - 1) * pageSize + 1)}</span>
          {' تا '}
          <span className="font-semibold text-ink-900 tabular-nums">{fa(Math.min(total, current * pageSize))}</span>
          {' از '}
          <span className="font-semibold text-ink-900 tabular-nums">{fa(total)}</span>
        </p>
      )
    ) : null;

  const atStart = current <= 1 || disabled;
  const atEnd = current >= count || disabled;

  return (
    <div className={cx('flex flex-wrap items-center justify-between gap-x-4 gap-y-2', className)}>
      {summary}
      {count > 1 && (
        <nav aria-label="صفحه‌بندی" className={cx('flex items-center gap-1', !summary && 'mx-auto')}>
          <button
            type="button"
            disabled={atStart}
            onClick={() => go(current - 1)}
            className={item({ disabled: atStart })}
            aria-label="صفحه‌ی قبلی"
          >
            <Chevron direction="previous" />
            <span className="hidden sm:inline">قبلی</span>
          </button>

          <span className="px-2 text-sm text-paper-600 tabular-nums sm:hidden" aria-live="polite">
            صفحه‌ی {fa(current)} از {fa(count)}
          </span>

          <span className="hidden items-center gap-1 sm:flex">
            {pageWindow(count, current).map((entry, index) =>
              entry === 'gap' ? (
                <span key={`gap-${index}`} className="w-6 text-center text-paper-400" aria-hidden>
                  …
                </span>
              ) : (
                <button
                  key={entry}
                  type="button"
                  disabled={disabled && entry !== current}
                  aria-label={`صفحه‌ی ${fa(entry)}`}
                  aria-current={entry === current ? 'page' : undefined}
                  onClick={() => go(entry)}
                  className={item({ active: entry === current })}
                >
                  {fa(entry)}
                </button>
              ),
            )}
          </span>

          <button
            type="button"
            disabled={atEnd}
            onClick={() => go(current + 1)}
            className={item({ disabled: atEnd })}
            aria-label="صفحه‌ی بعدی"
          >
            <span className="hidden sm:inline">بعدی</span>
            <Chevron direction="next" />
          </button>
        </nav>
      )}
    </div>
  );
}
