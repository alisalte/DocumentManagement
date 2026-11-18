import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router';
import { cx } from './cx';

export interface Crumb {
  label: ReactNode;
  /** A link when given; the last crumb is usually the page itself and has none. */
  to?: string;
  onClick?: () => void;
}

export interface PageHeaderProps {
  title: ReactNode;
  /** One line under the title saying what the page is for. */
  description?: ReactNode;
  /** Path above the title, e.g. «پوشه‌ها › قراردادها». */
  breadcrumbs?: Crumb[];
  /** Small count or status next to the title. */
  meta?: ReactNode;
  /** Buttons on the trailing side; they wrap under the title on a phone. */
  actions?: ReactNode;
  className?: string;
}

function Separator() {
  return (
    <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5 shrink-0 text-paper-400">
      <path
        fillRule="evenodd"
        d="M11.78 5.22a.75.75 0 0 1 0 1.06L8.06 10l3.72 3.72a.75.75 0 1 1-1.06 1.06l-4.25-4.25a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0Z"
        clipRule="evenodd"
      />
    </svg>
  );
}

/** The top of every page: breadcrumbs, the title (the page's only h1), a description and actions. */
export function PageHeader({ title, description, breadcrumbs, meta, actions, className }: PageHeaderProps) {
  return (
    <header className={cx('flex flex-wrap items-end justify-between gap-x-6 gap-y-3', className)}>
      <div className="min-w-0 flex-1">
        {breadcrumbs && breadcrumbs.length > 0 && (
          <nav aria-label="مسیر" className="mb-1.5">
            <ol className="flex flex-wrap items-center gap-1 text-xs text-paper-500">
              {breadcrumbs.map((crumb, index) => {
                const last = index === breadcrumbs.length - 1;
                const crumbClass = 'rounded-md px-1 py-0.5 transition-colors hover:bg-paper-200/70 hover:text-ink-900';
                return (
                  <li key={index} className="flex min-w-0 items-center gap-1">
                    {crumb.to ? (
                      <RouterLink to={crumb.to} className={crumbClass}>
                        {crumb.label}
                      </RouterLink>
                    ) : crumb.onClick ? (
                      <button type="button" onClick={crumb.onClick} className={crumbClass}>
                        {crumb.label}
                      </button>
                    ) : (
                      <span className={cx('truncate px-1', last && 'font-medium text-paper-700')}>{crumb.label}</span>
                    )}
                    {!last && <Separator />}
                  </li>
                );
              })}
            </ol>
          </nav>
        )}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <h1 className="min-w-0 text-xl font-bold tracking-tight text-ink-900 sm:text-2xl">{title}</h1>
          {meta}
        </div>
        {description && <p className="mt-1 max-w-3xl text-sm leading-6 text-paper-500">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}

export interface EmptyStateProps {
  icon?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}

/** What a list shows when it has nothing in it: an icon, one sentence and the next step. */
export function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
  return (
    <div className={cx('flex flex-col items-center px-6 py-14 text-center', className)}>
      <span className="mb-4 grid size-14 place-items-center rounded-2xl bg-paper-100 text-paper-400 ring-1 ring-paper-200 [&_svg]:size-7">
        {icon ?? (
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden>
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z"
            />
          </svg>
        )}
      </span>
      <p className="text-sm font-semibold text-ink-900">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm leading-6 text-paper-500">{description}</p>}
      {action && <div className="mt-5 flex flex-wrap justify-center gap-2">{action}</div>}
    </div>
  );
}
