import type { ReactNode } from 'react';
import type { DocumentListItem } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { t } from '../strings';
import { FileTypeBadge } from './FileTypeBadge';
import { Chip, cx } from './ui';

interface Props {
  items: DocumentListItem[];
  onOpen?: (item: DocumentListItem) => void;
  /** Resolve folder name from categoryId so rows show which folder the document lives in. */
  categoryNameOf?: (categoryId: string) => string | null | undefined;
  /** Extra per-row content, e.g. a restore button in the recycle bin. */
  renderAction?: (item: DocumentListItem) => ReactNode;
  dateOf?: (item: DocumentListItem) => string;
  dateLabel?: string;
}

/**
 * One scannable row per document at any width: title first, folder + file details underneath,
 * version, date and any per-row action on the trailing side. The whole row opens the document.
 */
export function DocumentList({
  items,
  onOpen,
  categoryNameOf,
  renderAction,
  dateOf,
  dateLabel = t.updatedAt,
}: Props) {
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
      {items.map((item) => {
        const folder = categoryNameOf?.(item.categoryId)?.trim() || null;
        const details = [item.fileName, formatBytes(item.fileSize)].filter(Boolean).join(' · ');

        return (
          <li key={item.id}>
            <div
              onClick={() => onOpen?.(item)}
              className={cx(
                'flex flex-wrap items-center gap-x-4 gap-y-2 px-3 py-3.5 transition-colors duration-150 sm:px-4',
                onOpen && 'cursor-pointer hover:bg-ink-50/70',
              )}
            >
              <FileTypeBadge fileName={item.fileName} mimeType={item.mimeType} />

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
                  {[folder ? `${t.category}: ${folder}` : null, details].filter(Boolean).join(' · ')}
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
        );
      })}
    </ul>
  );
}
