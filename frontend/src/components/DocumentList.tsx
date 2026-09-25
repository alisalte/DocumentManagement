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
    return <p className="py-10 text-center text-sm text-slate-500">{t.noDocuments}</p>;
  }

  return (
    <ul className="divide-y divide-slate-100">
      {items.map((item) => (
        <li key={item.id}>
          <div
            onClick={() => onOpen?.(item)}
            className={cx(
              'flex flex-wrap items-center gap-x-4 gap-y-2 px-3 py-3 transition-colors sm:px-4',
              onOpen && 'cursor-pointer hover:bg-slate-50',
            )}
          >
            <div className="w-full min-w-0 sm:w-auto sm:flex-1">
              <button
                type="button"
                disabled={!onOpen}
                onClick={(event) => {
                  event.stopPropagation();
                  onOpen?.(item);
                }}
                className="block w-full rounded text-start focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600"
              >
                <span className="block truncate text-sm font-semibold text-slate-800">{item.title}</span>
              </button>
              <p className="mt-0.5 truncate text-xs text-slate-500">
                {[item.fileName, formatBytes(item.fileSize)].filter(Boolean).join(' · ')}
              </p>
            </div>

            {item.currentVersionLabel && (
              <Chip label={item.currentVersionLabel} variant="outlined" dir="ltr" className="shrink-0" />
            )}

            <span title={dateLabel} className="shrink-0 text-xs whitespace-nowrap text-slate-500">
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
