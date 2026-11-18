import type { ReactNode } from 'react';
import type { DocumentListItem } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { t } from '../strings';
import { FileTypeBadge } from './FileTypeBadge';
import { FolderOutlineIcon } from './icons';
import { Chip, EmptyState, cx } from './ui';

interface Props {
  items: DocumentListItem[];
  onOpen?: (item: DocumentListItem) => void;
  /** Resolve folder name from categoryId so rows show which folder the document lives in. */
  categoryNameOf?: (categoryId: string) => string | null | undefined;
  /** Extra per-row content, e.g. a restore button in the recycle bin. */
  renderAction?: (item: DocumentListItem) => ReactNode;
  dateOf?: (item: DocumentListItem) => string;
  dateLabel?: string;
  /** Secondary empty-state line; defaults to the folder-browse copy. */
  emptyHint?: string;
  /** Next step shown under an empty list, e.g. a «new document» button. */
  emptyAction?: ReactNode;
}

/**
 * Documents as a table on a desktop (name, folder, version, size, date) and as stacked cards on a
 * phone. The whole row opens the document; the title is the keyboard target.
 */
export function DocumentList({
  items,
  onOpen,
  categoryNameOf,
  renderAction,
  dateOf,
  dateLabel = t.updatedAt,
  emptyHint,
  emptyAction,
}: Props) {
  const dateFor = dateOf ?? ((item: DocumentListItem) => item.updatedAt);
  const columns = cx(
    'md:grid md:items-center md:gap-4',
    renderAction
      ? 'md:grid-cols-[minmax(0,1fr)_minmax(0,11rem)_4.5rem_5.5rem_8.5rem_auto]'
      : 'md:grid-cols-[minmax(0,1fr)_minmax(0,11rem)_4.5rem_5.5rem_8.5rem]',
  );

  if (items.length === 0) {
    return (
      <EmptyState
        title={t.noDocuments}
        description={emptyHint ?? 'هنوز سندی در این پوشه ثبت نشده است.'}
        action={emptyAction}
      />
    );
  }

  return (
    <div role="table" aria-label="فهرست اسناد" className="w-full">
      <div role="rowgroup" className="hidden border-b border-paper-200 bg-paper-100/60 md:block">
        <div role="row" className={cx(columns, 'px-5 py-2.5 text-xs font-semibold text-paper-500')}>
          <span role="columnheader">{t.title}</span>
          <span role="columnheader">{t.category}</span>
          <span role="columnheader">نسخه</span>
          <span role="columnheader">حجم</span>
          <span role="columnheader">{dateLabel}</span>
          {renderAction && (
            <span role="columnheader" className="sr-only">
              عملیات
            </span>
          )}
        </div>
      </div>

      <ul role="rowgroup" className="divide-y divide-paper-200/70">
        {items.map((item) => {
          const folder = categoryNameOf?.(item.categoryId)?.trim() || null;
          const fileLabel = item.fileName?.trim() || null;
          const size = item.fileSize != null ? formatBytes(item.fileSize) : null;

          return (
            <li key={item.id} role="row">
              <div
                onClick={() => onOpen?.(item)}
                className={cx(
                  columns,
                  'group flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3.5 transition-colors duration-150 md:px-5 md:py-3',
                  onOpen && 'cursor-pointer hover:bg-paper-100/80',
                )}
              >
                {/* Name: file badge, title and file name. */}
                <div role="cell" className="flex min-w-0 flex-1 items-center gap-3">
                  <FileTypeBadge fileName={item.fileName} mimeType={item.mimeType} />
                  <div className="min-w-0 flex-1">
                    <button
                      type="button"
                      disabled={!onOpen}
                      onClick={(event) => {
                        event.stopPropagation();
                        onOpen?.(item);
                      }}
                      className="block max-w-full rounded text-start focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-500"
                    >
                      <span className="line-clamp-2 text-sm font-semibold text-ink-900 transition-colors group-hover:text-ink-700 md:line-clamp-1">
                        {item.title}
                      </span>
                    </button>
                    {fileLabel && (
                      <p className="mt-0.5 truncate text-xs text-paper-500" title={fileLabel}>
                        <bdi>{fileLabel}</bdi>
                      </p>
                    )}
                    {item.deleteReason && (
                      <p className="mt-0.5 line-clamp-2 text-xs text-rose-600 dark:text-rose-300">
                        {t.deleteReason}: {item.deleteReason}
                      </p>
                    )}
                  </div>
                </div>

                {/* Folder. On a phone it joins the meta line under the title. */}
                <div role="cell" className="hidden min-w-0 md:block">
                  {folder ? (
                    <span className="inline-flex max-w-full items-center gap-1.5 text-xs text-paper-600" title={folder}>
                      <FolderOutlineIcon className="size-4 shrink-0 text-paper-400" />
                      <span className="truncate">{folder}</span>
                    </span>
                  ) : (
                    <span className="text-xs text-paper-400">—</span>
                  )}
                </div>

                <div role="cell" className="hidden md:block">
                  {item.currentVersionLabel ? (
                    <Chip label={item.currentVersionLabel} variant="outlined" dir="ltr" />
                  ) : (
                    <span className="text-xs text-paper-400">—</span>
                  )}
                </div>

                <div role="cell" className="hidden text-xs text-paper-600 tabular-nums md:block">
                  {size ?? '—'}
                </div>

                <div role="cell" className="hidden text-xs whitespace-nowrap text-paper-600 tabular-nums md:block" title={dateLabel}>
                  {formatDateTime(dateFor(item))}
                </div>

                {/* Phone meta line. */}
                <div className="flex w-full flex-wrap items-center gap-x-2 gap-y-1 ps-12 text-[11px] text-paper-500 md:hidden">
                  {item.currentVersionLabel && (
                    <Chip label={item.currentVersionLabel} variant="outlined" dir="ltr" className="h-5 px-1.5 text-[10px]" />
                  )}
                  {folder && <span className="max-w-[12rem] truncate">{folder}</span>}
                  {size && (
                    <>
                      <span aria-hidden>·</span>
                      <span className="tabular-nums">{size}</span>
                    </>
                  )}
                  <span aria-hidden>·</span>
                  <span className="tabular-nums" title={dateLabel}>
                    {formatDateTime(dateFor(item))}
                  </span>
                </div>

                {renderAction && (
                  <div
                    role="cell"
                    onClick={(event) => event.stopPropagation()}
                    className="flex w-full shrink-0 items-center justify-end ps-12 md:w-auto md:ps-0"
                  >
                    {renderAction(item)}
                  </div>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
