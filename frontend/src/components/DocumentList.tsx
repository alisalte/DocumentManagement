import type { ReactNode } from 'react';
import type { DocumentListItem } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { t } from '../strings';
import { Chip, cx } from './ui';

interface Props {
  items: DocumentListItem[];
  onOpen?: (item: DocumentListItem) => void;
  /** Extra per-row content, e.g. a restore button in the recycle bin. */
  renderAction?: (item: DocumentListItem) => ReactNode;
  dateOf?: (item: DocumentListItem) => string;
  dateLabel?: string;
}

/**
 * One scannable row per document at any width: title first, the file's details underneath,
 * version, date and any per-row action on the trailing side. The whole row opens the document.
 */
export function DocumentList({ items, onOpen, renderAction, dateOf, dateLabel = t.updatedAt }: Props) {
  const dateFor = dateOf ?? ((item: DocumentListItem) => item.updatedAt);

  if (items.length === 0) {
    return (
      <div className="px-4 py-14 text-center">
        <p className="text-sm font-medium text-paper-600">{t.noDocuments}</p>
        <p className="mt-1 text-xs text-paper-400">هنوز سندی در این پوشه ثبت نشده است.</p>
      </div>
    );
  }

  return (
    <ul className="divide-y divide-paper-100">
      {items.map((item) => (
        <li key={item.id}>
          <div
            onClick={() => onOpen?.(item)}
            className={cx(
              'flex flex-wrap items-center gap-x-4 gap-y-2 px-3 py-3.5 transition-colors duration-150 sm:px-4',
              onOpen && 'cursor-pointer hover:bg-ink-50/70',
            )}
          >
            <div className="grid size-9 shrink-0 place-items-center rounded-lg bg-ink-50 text-ink-700">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6} aria-hidden className="size-4">
                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" />
              </svg>
            </div>

            <div className="w-full min-w-0 sm:w-auto sm:flex-1">
              <button
                type="button"
                disabled={!onOpen}
                onClick={(event) => {
                  event.stopPropagation();
                  onOpen?.(item);
                }}
                className="block w-full rounded text-start focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-600"
              >
                <span className="block truncate text-sm font-semibold text-ink-900">{item.title}</span>
              </button>
              <p className="mt-0.5 truncate text-xs text-paper-500">
                {[item.fileName, formatBytes(item.fileSize)].filter(Boolean).join(' · ')}
              </p>
            </div>

            {item.currentVersionLabel && (
              <Chip label={item.currentVersionLabel} variant="outlined" dir="ltr" className="shrink-0" />
            )}

            <span title={dateLabel} className="shrink-0 text-xs whitespace-nowrap text-paper-500">
              {formatDateTime(dateFor(item))}
            </span>

            {renderAction && (
              <div onClick={(event) => event.stopPropagation()} className="flex shrink-0 items-center">
                {renderAction(item)}
              </div>
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}
