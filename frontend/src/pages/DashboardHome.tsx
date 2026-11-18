import { Alert, Button, Card, Chip, ProgressBar, cx } from '../components/ui';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router';
import { FileTypeBadge } from '../components/FileTypeBadge';
import { FolderIcon } from '../components/FolderIcon';
import { ArrowBackIcon, CalendarIcon, FolderOutlineIcon, InboxIcon, PlusIcon, SearchIcon, ShareIcon } from '../components/icons';
import { api, type CategoryNode, type DocumentListItem, type UploadResult, type WorkflowTask } from '../lib/api';
import { useFiscalYear } from '../lib/fiscalYear';
import { subtreeFileCounts, topLevelArchiveFolders, visibleChildFolders } from '../lib/categories';
import { formatDate, formatDateTime } from '../lib/dates';
import { formatBytes, formatNumber, newIdempotencyKey } from '../lib/format';
import { useSession } from '../session';
import { w } from '../components/workflow/workflowStrings';
import { describeError, t } from '../strings';

type QueueStatus = 'queued' | 'uploading' | 'done' | 'error';

interface QueueItem {
  id: string;
  file: File;
  progress: number;
  status: QueueStatus;
  error?: string;
  upload?: UploadResult;
}

function DocumentStackIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.7} aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" />
    </svg>
  );
}

function CloudIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.7} aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15a4.5 4.5 0 0 0 4.5 4.5H18a3.75 3.75 0 0 0 1.332-7.257 3 3 0 0 0-3.758-3.848 5.25 5.25 0 0 0-10.233 2.33A4.502 4.502 0 0 0 2.25 15Z" />
    </svg>
  );
}

function greetingForHour(hour: number): string {
  if (hour < 5) return 'شب‌بخیر';
  if (hour < 12) return 'صبح بخیر';
  if (hour < 17) return 'ظهر بخیر';
  if (hour < 21) return 'عصر بخیر';
  return 'شب‌بخیر';
}

const metricTones = {
  ink: 'bg-ink-50 text-ink-600 ring-ink-100',
  amber: 'bg-amber-50 text-amber-600 ring-amber-100 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-500/20',
  emerald: 'bg-emerald-50 text-emerald-600 ring-emerald-100 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/20',
  sky: 'bg-sky-50 text-sky-600 ring-sky-100 dark:bg-sky-500/10 dark:text-sky-300 dark:ring-sky-500/20',
} as const;

function Metric({
  label,
  value,
  hint,
  to,
  tone,
  icon,
  delay,
}: {
  label: string;
  value: string;
  hint?: string;
  to?: string;
  tone: keyof typeof metricTones;
  icon: ReactNode;
  delay: number;
}) {
  const body = (
    <>
      <div className="flex items-start justify-between gap-3">
        <p className="text-[13px] font-medium text-paper-500">{label}</p>
        <span className={cx('grid size-9 shrink-0 place-items-center rounded-xl ring-1 ring-inset [&_svg]:size-5', metricTones[tone])}>
          {icon}
        </span>
      </div>
      <p className="mt-1 text-2xl font-bold tracking-tight text-ink-900 tabular-nums sm:text-[28px]">{value}</p>
      {hint && <p className="mt-1 truncate text-xs text-paper-500">{hint}</p>}
    </>
  );

  const className = cx(
    'dashboard-stagger block rounded-2xl border border-paper-200 bg-paper-50 p-4 shadow-[var(--shadow-surface)] transition-all duration-200 sm:p-5',
    to && 'hover:-translate-y-0.5 hover:border-paper-300 hover:shadow-[var(--shadow-elevated)]',
  );

  if (to) {
    return (
      <RouterLink to={to} className={className} style={{ animationDelay: `${delay}ms` }}>
        {body}
      </RouterLink>
    );
  }

  return (
    <div className={className} style={{ animationDelay: `${delay}ms` }}>
      {body}
    </div>
  );
}

function FolderCard({
  category,
  countLabel,
  hasChildren,
  onOpen,
  onOpenDocuments,
  index,
}: {
  category: CategoryNode;
  countLabel: string;
  hasChildren: boolean;
  onOpen: () => void;
  onOpenDocuments: () => void;
  index: number;
}) {
  return (
    <div
      style={{ animationDelay: `${80 + index * 30}ms` }}
      className="dashboard-stagger group relative flex items-center gap-3 rounded-2xl border border-paper-200 bg-paper-50 p-3 pe-2 shadow-[var(--shadow-surface)] transition-all duration-200 hover:-translate-y-0.5 hover:border-ink-200 hover:shadow-[var(--shadow-elevated)]"
    >
      <button type="button" onClick={onOpen} className="flex min-w-0 flex-1 items-center gap-3 text-start after:absolute after:inset-0 after:rounded-2xl">
        <FolderIcon className="h-10 w-12 shrink-0 transition-transform duration-300 group-hover:scale-105" />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-semibold text-ink-900">{category.name}</span>
          <span className="mt-0.5 block text-xs text-paper-500 tabular-nums">
            {countLabel}
            {hasChildren && ' · دارای زیرپوشه'}
            {!category.canCreate && ' · فقط مشاهده'}
          </span>
        </span>
      </button>
      {hasChildren ? (
        <button
          type="button"
          onClick={onOpenDocuments}
          className="relative z-10 shrink-0 rounded-lg px-2.5 py-1.5 text-xs font-medium text-ink-600 opacity-80 transition-colors hover:bg-ink-50 hover:opacity-100"
        >
          مشاهده اسناد
        </button>
      ) : (
        <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-4 shrink-0 text-paper-400 transition-transform group-hover:-translate-x-0.5">
          <path fillRule="evenodd" d="M11.78 5.22a.75.75 0 0 1 0 1.06L8.06 10l3.72 3.72a.75.75 0 1 1-1.06 1.06l-4.25-4.25a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0Z" clipRule="evenodd" />
        </svg>
      )}
    </div>
  );
}

function FolderGrid({
  categories,
  roots,
  countLabel,
  onOpenDocuments,
  filingTarget,
}: {
  categories: CategoryNode[];
  roots: CategoryNode[];
  countLabel: (id: string) => string;
  onOpenDocuments: (id: string) => void;
  filingTarget: string | undefined;
}) {
  const [trail, setTrail] = useState<string[]>([]);

  useEffect(() => {
    if (trail.some((id) => !categories.some((category) => category.id === id))) {
      setTrail([]);
    }
  }, [categories, trail]);

  const focusId = trail[trail.length - 1];
  const focus = focusId ? categories.find((category) => category.id === focusId) : undefined;
  const shown = focusId ? visibleChildFolders(categories, focusId) : roots;

  const openFolder = (category: CategoryNode) => {
    if (visibleChildFolders(categories, category.id).length > 0) {
      setTrail((current) => [...current, category.id]);
      return;
    }
    onOpenDocuments(category.id);
  };

  return (
    <section className="dashboard-stagger" style={{ animationDelay: '80ms' }}>
      <div className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          {trail.length > 0 && (
            <button
              type="button"
              onClick={() => setTrail((current) => current.slice(0, -1))}
              className="mb-1 inline-flex items-center gap-1 rounded-md text-xs font-medium text-ink-600 hover:text-ink-800"
            >
              <ArrowBackIcon className="size-3.5" />
              بازگشت
            </button>
          )}
          <h2 className="text-base font-bold text-ink-900">{focus?.name ?? 'پوشه‌های بایگانی'}</h2>
          <p className="mt-0.5 text-xs text-paper-500">
            {focus
              ? 'زیرپوشه‌های این پوشه؛ عدد هر کارت تعداد فایل‌های داخل آن است.'
              : 'با کلیک روی هر پوشه، زیرپوشه‌ها یا اسناد آن باز می‌شود.'}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {focus && (
            <Button size="sm" variant="outline" onClick={() => onOpenDocuments(focus.id)}>
              اسناد این پوشه
            </Button>
          )}
          {filingTarget && !focus && (
            <Button as={RouterLink} to={`/?category=${filingTarget}`} size="sm" variant="outline">
              پرونده‌های من
            </Button>
          )}
        </div>
      </div>
      {shown.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-paper-500">پوشه‌ای برای نمایش نیست.</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 2xl:grid-cols-3">
          {shown.map((cat, index) => (
            <FolderCard
              key={cat.id}
              category={cat}
              countLabel={countLabel(cat.id)}
              hasChildren={visibleChildFolders(categories, cat.id).length > 0}
              onOpen={() => openFolder(cat)}
              onOpenDocuments={() => onOpenDocuments(cat.id)}
              index={index}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function RecentDocs({
  items,
  loading,
  error,
  categoryNameOf,
}: {
  items: DocumentListItem[];
  loading: boolean;
  error: unknown;
  categoryNameOf?: (categoryId: string) => string | null | undefined;
}) {
  const navigate = useNavigate();

  return (
    <section className="dashboard-stagger overflow-hidden rounded-2xl border border-paper-200 bg-paper-50 shadow-[var(--shadow-surface)]" style={{ animationDelay: '120ms' }}>
      <div className="flex items-center justify-between gap-3 border-b border-paper-200 px-4 py-3.5 sm:px-5">
        <div>
          <h2 className="text-base font-bold text-ink-900">اسناد اخیر</h2>
          <p className="text-xs text-paper-500">آخرین پرونده‌هایی که به آن‌ها دسترسی دارید</p>
        </div>
        <Button as={RouterLink} to="/search" size="sm" variant="outline">
          مشاهده همه
        </Button>
      </div>
      {loading && <ProgressBar />}
      {error ? (
        <div className="p-4">
          <Alert severity="error">{describeError(error)}</Alert>
        </div>
      ) : items.length === 0 ? (
        <div className="px-4 py-10 text-center">
          <p className="text-sm text-paper-500">{t.noDocuments}</p>
          <Button as={RouterLink} to="/new" size="sm" className="mt-3">
            {t.newDocument}
          </Button>
        </div>
      ) : (
        <ul className="divide-y divide-paper-200/70">
          {items.map((item) => {
            const folder = categoryNameOf?.(item.categoryId)?.trim() || null;
            return (
              <li key={item.id}>
                <button
                  type="button"
                  onClick={() => navigate(`/documents/${item.id}`)}
                  className="flex w-full items-center gap-3 px-4 py-3 text-start transition-colors hover:bg-paper-100/80 sm:px-5"
                >
                  <FileTypeBadge fileName={item.fileName} mimeType={item.mimeType} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-ink-900">{item.title}</p>
                    <p className="mt-0.5 truncate text-xs text-paper-500">
                      {[
                        folder ? `${t.category}: ${folder}` : null,
                        item.fileName,
                        formatBytes(item.fileSize),
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </p>
                  </div>
                  <div className="shrink-0 text-end">
                    {item.currentVersionLabel && (
                      <Chip size="small" variant="outlined" label={item.currentVersionLabel} dir="ltr" className="mb-1" />
                    )}
                    <p className="text-[11px] text-paper-500 tabular-nums">{formatDateTime(item.updatedAt)}</p>
                  </div>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

function TasksPanel({ tasks, loading }: { tasks: WorkflowTask[]; loading: boolean }) {
  const pending = tasks.filter((task) => task.status === 'Pending');
  const preview = pending.slice(0, 4);

  return (
    <section className="dashboard-stagger overflow-hidden rounded-2xl border border-paper-200 bg-paper-50 shadow-[var(--shadow-surface)]" style={{ animationDelay: '160ms' }}>
      <div className="flex items-center justify-between border-b border-paper-200 px-4 py-3">
        <div>
          <h2 className="text-sm font-bold text-ink-900">{w.inbox}</h2>
          <p className="text-[11px] text-paper-500">کارهای در انتظار تأیید شما</p>
        </div>
        {pending.length > 0 && (
          <Chip size="small" color="primary" label={formatNumber(pending.length)} />
        )}
      </div>
      {loading && <ProgressBar />}
      {!loading && preview.length === 0 && (
        <p className="px-4 py-6 text-center text-sm text-paper-500">کار بازی در کارتابل نیست.</p>
      )}
      {preview.length > 0 && (
        <ul className="divide-y divide-paper-200/70">
          {preview.map((task) => (
            <li key={task.id}>
              <RouterLink
                to="/tasks"
                className="block px-4 py-3 transition-colors hover:bg-paper-100/80"
              >
                <p className="truncate text-sm font-medium text-ink-900">{task.documentTitle}</p>
                <p className="mt-0.5 truncate text-xs text-paper-500">
                  {task.stepName}
                  {task.isOverdue ? ' · سررسید گذشته' : ''}
                </p>
              </RouterLink>
            </li>
          ))}
        </ul>
      )}
      <div className="border-t border-paper-200 p-3">
        <Button as={RouterLink} to="/tasks" fullWidth size="sm" variant="outline">
          باز کردن کارتابل
        </Button>
      </div>
    </section>
  );
}

function UploadRail({
  filingTarget,
  closed,
}: {
  filingTarget: string | undefined;
  closed: boolean;
}) {
  const navigate = useNavigate();
  const [queue, setQueue] = useState<QueueItem[]>([]);
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const activeIds = useRef(new Set<string>());

  const updateItem = (id: string, patch: Partial<QueueItem>) => {
    setQueue((current) => current.map((item) => (item.id === id ? { ...item, ...patch } : item)));
  };

  const startUpload = (item: QueueItem) => {
    if (activeIds.current.has(item.id)) return;
    activeIds.current.add(item.id);
    updateItem(item.id, { status: 'uploading', progress: 0, error: undefined });
    void api
      .upload(item.file, (fraction) => updateItem(item.id, { progress: Math.round(fraction * 100) }))
      .then((upload) => updateItem(item.id, { status: 'done', progress: 100, upload }))
      .catch((error: unknown) => {
        updateItem(item.id, { status: 'error', error: describeError(error), progress: 0 });
      })
      .finally(() => activeIds.current.delete(item.id));
  };

  const enqueue = (files: FileList | File[]) => {
    const list = Array.from(files);
    if (list.length === 0) return;
    const next: QueueItem[] = list.map((file) => ({
      id: `${file.name}-${file.size}-${file.lastModified}-${newIdempotencyKey()}`,
      file,
      progress: 0,
      status: 'queued' as const,
    }));
    setQueue((current) => [...next, ...current]);
    for (const item of next) startUpload(item);
  };

  const newDocHref = filingTarget ? `/new?category=${filingTarget}` : '/new';
  const busyCount = queue.filter((item) => item.status === 'queued' || item.status === 'uploading').length;
  const readyUploads = queue.filter(
    (item) => item.status === 'done' && item.upload && item.upload.duplicates.length === 0,
  );

  const registerUpload = (upload: UploadResult) => {
    navigate(newDocHref, { state: { stagedUpload: upload } });
  };

  if (closed) {
    return (
      <Card className="border-dashed border-paper-300 bg-paper-50">
        <p className="text-sm font-semibold text-ink-900">بارگذاری سریع</p>
        <p className="mt-2 text-xs leading-5 text-paper-600">{t.fiscalYearQuickUploadClosed}</p>
      </Card>
    );
  }

  return (
    <div className="dashboard-stagger space-y-3" style={{ animationDelay: '80ms' }}>
      <Card
        className={cx(
          'border-2 border-dashed transition-colors',
          dragOver ? 'border-ink-400 bg-ink-50' : 'border-paper-300 hover:border-ink-300',
        )}
        onDragOver={(event) => {
          event.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(event) => {
          event.preventDefault();
          setDragOver(false);
          enqueue(event.dataTransfer.files);
        }}
      >
        <div className="flex flex-col items-center py-3 text-center">
          <span className="mb-3 grid size-12 place-items-center rounded-2xl bg-ink-50 text-ink-600 ring-1 ring-ink-100">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} className="size-7">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 16.5V9.75m0 0 3 3m-3-3-3 3M6.75 19.5a4.5 4.5 0 0 1-1.41-8.775 5.25 5.25 0 0 1 10.233-2.33 4.5 4.5 0 0 1 4.5 4.5v.008H19.5a3 3 0 0 1 3 3v1.5a3 3 0 0 1-3 3H6.75Z"
              />
            </svg>
          </span>
          <p className="text-sm font-semibold text-ink-900">بارگذاری سریع</p>
          <p className="mt-1 max-w-[15rem] text-xs leading-5 text-paper-500">
            فایل را رها کنید؛ بعد با «ثبت سند» آن را در پوشه بایگانی کنید.
          </p>
          <input
            ref={inputRef}
            type="file"
            multiple
            hidden
            onChange={(event) => {
              if (event.target.files) enqueue(event.target.files);
              event.target.value = '';
            }}
          />
          <Button className="mt-4" size="sm" variant="outline" onClick={() => inputRef.current?.click()}>
            {t.chooseFile}
          </Button>
        </div>
      </Card>

      {queue.length > 0 && (
      <Card flush className="overflow-hidden">
        <div className="flex items-center justify-between border-b border-paper-200 px-4 py-3">
          <p className="text-xs font-semibold text-paper-600">بارگذاری‌های جاری</p>
          {busyCount > 0 && (
            <span className="text-[10px] font-semibold text-ink-600">{formatNumber(busyCount)} در حال ارسال</span>
          )}
        </div>
        {(
          <ul className="max-h-64 divide-y divide-paper-200 overflow-y-auto">
            {queue.map((item) => (
              <li key={item.id} className="flex items-start gap-3 px-4 py-3">
                <FileTypeBadge fileName={item.file.name} mimeType={item.file.type} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm text-ink-900" dir="ltr">
                    {item.file.name}
                  </p>
                  <p className="text-xs text-paper-500">{formatBytes(item.file.size)}</p>
                  {item.status === 'error' ? (
                    <p className="mt-1 text-xs text-rose-600">{item.error ?? t.retry}</p>
                  ) : (
                    <>
                      <div className="mt-2 h-1 overflow-hidden rounded-full bg-paper-200">
                        <div
                          className={cx(
                            'h-full rounded-full transition-[width] duration-200',
                            item.status === 'done' ? 'bg-emerald-500' : 'bg-ink-500',
                          )}
                          style={{ width: `${item.progress}%` }}
                        />
                      </div>
                      <p className="mt-1 text-[10px] text-paper-500">
                        {item.status === 'done'
                          ? item.upload && item.upload.duplicates.length > 0
                            ? 'قبلاً ثبت شده'
                            : 'آمادهٔ ثبت سند'
                          : `${formatNumber(item.progress)}٪`}
                      </p>
                    </>
                  )}
                  {item.status === 'done' && item.upload && item.upload.duplicates.length === 0 && (
                    <button
                      type="button"
                      className="mt-1 text-xs font-medium text-ink-700 hover:text-ink-900"
                      onClick={() => registerUpload(item.upload!)}
                    >
                      {t.submit}
                    </button>
                  )}
                  {item.status === 'done' && item.upload && item.upload.duplicates.length > 0 && (
                    <p className="mt-1 text-xs text-rose-700">
                      {t.duplicateNotice}{' '}
                      {item.upload.duplicates.map((duplicate, index) => (
                        <span key={duplicate.documentId}>
                          {index > 0 && '، '}
                          <RouterLink to={`/documents/${duplicate.documentId}`} className="font-medium text-ink-700 hover:underline">
                            {duplicate.title}
                          </RouterLink>
                        </span>
                      ))}
                    </p>
                  )}
                  {item.status === 'error' && (
                    <button
                      type="button"
                      className="mt-1 text-xs font-medium text-ink-600 hover:text-ink-800"
                      onClick={() => startUpload(item)}
                    >
                      {t.retry}
                    </button>
                  )}
                </div>
                <button
                  type="button"
                  className="grid size-7 shrink-0 place-items-center rounded-lg text-paper-400 transition-colors hover:bg-rose-50 hover:text-rose-600"
                  aria-label="حذف"
                  onClick={() => {
                    setQueue((current) => current.filter((entry) => entry.id !== item.id));
                    activeIds.current.delete(item.id);
                  }}
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}
      </Card>
      )}

      {readyUploads.length > 0 && (
        <Button fullWidth size="lg" onClick={() => registerUpload(readyUploads[0]!.upload!)}>
          ادامهٔ ثبت سند
        </Button>
      )}
    </div>
  );
}

/**
 * Content-rich home for «مدیریت خودکار»: greeting, live stats, recent docs, folders,
 * upload rail, and inbox — using the existing Fillo surface language.
 */
export function DashboardHome({
  categories,
  filingTarget,
  onOpenCategory,
  demo = false,
  directFileCounts,
}: {
  categories: CategoryNode[];
  filingTarget: string | undefined;
  onOpenCategory: (id: string) => void;
  demo?: boolean;
  /** Direct file counts used by the demo dashboard. Live data comes from the counts API. */
  directFileCounts?: Record<string, number>;
}) {
  const { user } = useSession();
  const fiscal = useFiscalYear();
  const fiscalYear = !demo && fiscal.ready && fiscal.selectedYear > 0 ? fiscal.selectedYear : undefined;
  // Real folders sit under the fixed archive root («اسناد»), not at parentId null.
  const roots = useMemo(() => topLevelArchiveFolders(categories), [categories]);

  const categoryCounts = useQuery({
    queryKey: ['document-counts-by-category', fiscalYear ?? null],
    queryFn: () => api.documentCounts(fiscalYear),
    enabled: !demo && (fiscal.ready || fiscal.failed),
    staleTime: 30_000,
  });

  const recent = useQuery({
    queryKey: ['dashboard-recent', fiscalYear ?? null],
    queryFn: () => api.documents({ page: 1, pageSize: 8, includeSubcategories: true, fiscalYear }),
    enabled: !demo && (fiscal.ready || fiscal.failed),
    staleTime: 30_000,
  });
  const tasks = useQuery({
    queryKey: ['tasks'],
    queryFn: api.workflow.tasks,
    enabled: !demo,
    refetchInterval: 60_000,
  });
  const storage = useQuery({
    queryKey: ['storage-usage'],
    queryFn: api.storageUsage,
    enabled: !demo,
    retry: false,
    staleTime: 60_000,
  });
  const shares = useQuery({
    queryKey: ['shares-received'],
    queryFn: api.sharing.received,
    enabled: !demo,
    staleTime: 60_000,
  });
  const recycle = useQuery({
    queryKey: ['recycle-bin-count'],
    queryFn: () => api.recycleBin(1),
    enabled: !demo,
    staleTime: 60_000,
  });

  const today = formatDate(new Date().toISOString());
  const hello = greetingForHour(new Date().getHours());
  const displayName = user?.displayName || user?.username || 'کاربر';
  const docTotal = demo ? 128 : (recent.data?.total ?? 0);
  const folderTotal = categories.filter((c) => c.canView).length;
  const taskTotal = demo ? 3 : (tasks.data?.filter((task) => task.status === 'Pending').length ?? 0);
  const shareTotal = demo ? 2 : (shares.data?.length ?? 0);
  const usedBytes = demo ? 2_400_000_000 : (storage.data?.usedBytes ?? 0);
  const quotaBytes = demo ? 10_000_000_000 : (storage.data?.quotaBytes ?? 0);
  const storageHint =
    storage.isError && !demo
      ? 'فضا در دسترس نیست'
      : quotaBytes > 0
        ? `${formatBytes(usedBytes)} از ${formatBytes(quotaBytes)}`
        : formatBytes(usedBytes) || '—';
  const storagePct = quotaBytes > 0 ? Math.min(100, Math.round((usedBytes / quotaBytes) * 100)) : 0;

  const directCounts = useMemo(() => {
    if (demo) return directFileCounts ?? {};
    const map: Record<string, number> = {};
    for (const row of categoryCounts.data ?? []) map[row.categoryId] = row.count;
    return map;
  }, [demo, directFileCounts, categoryCounts.data]);
  const fileTotals = useMemo(
    () => subtreeFileCounts(categories, directCounts),
    [categories, directCounts],
  );
  const countLabel = (id: string) => {
    if (!demo && categoryCounts.isLoading) return '…';
    if (!demo && categoryCounts.isError) return '—';
    return `${formatNumber(fileTotals[id] ?? 0)} فایل`;
  };

  const recentItems = demo
    ? ([
        {
          id: 'demo-1',
          title: 'قرارداد خدمات ۱۴۰۴',
          categoryId: 'd1',
          documentTypeId: 't',
          ownerId: 'u',
          currentVersionLabel: 'V1.2',
          fileName: 'contract.pdf',
          mimeType: 'application/pdf',
          fileSize: 240_000,
          updatedAt: new Date().toISOString(),
          deletedAt: null,
          deleteReason: null,
          fiscalYear: 1404,
        },
        {
          id: 'demo-2',
          title: 'گزارش فصلی فروش',
          categoryId: 'd1',
          documentTypeId: 't',
          ownerId: 'u',
          currentVersionLabel: 'V2.1',
          fileName: 'report.docx',
          mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          fileSize: 88_000,
          updatedAt: new Date(Date.now() - 86_400_000).toISOString(),
          deletedAt: null,
          deleteReason: null,
          fiscalYear: 1405,
        },
      ] satisfies DocumentListItem[])
    : (recent.data?.items ?? []);

  return (
    <div className="space-y-6 page-enter">
      <section className="dashboard-stagger relative overflow-hidden rounded-2xl border border-paper-200 bg-paper-50 shadow-[var(--shadow-surface)]">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{
            backgroundImage:
              'radial-gradient(60rem 18rem at 100% 0%, rgb(99 102 241 / 0.10), transparent 60%), radial-gradient(30rem 14rem at 0% 100%, rgb(14 165 233 / 0.06), transparent 60%)',
          }}
        />
        <div className="relative flex flex-col gap-5 p-5 sm:p-6 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0">
            <p className="inline-flex items-center gap-1.5 rounded-full bg-paper-100 px-2.5 py-1 text-xs font-medium text-paper-600 ring-1 ring-paper-200">
              <CalendarIcon className="size-3.5" />
              {today}
            </p>
            <h1 className="mt-3 text-2xl font-bold tracking-tight text-ink-900 sm:text-[28px]">
              {hello}، {displayName}
            </h1>
            <p className="mt-1.5 max-w-xl text-sm leading-6 text-paper-500">
              وضعیت بایگانی، کارتابل و بارگذاری‌های امروز را یک‌جا ببینید.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {!fiscal.closed && (
              <Button as={RouterLink} to={filingTarget ? `/new?category=${filingTarget}` : '/new'} size="lg">
                <PlusIcon strokeWidth={2.2} />
                {t.newDocument}
              </Button>
            )}
            <Button as={RouterLink} to="/search" size="lg" variant="outline">
              <SearchIcon />
              {t.searchEverything}
            </Button>
          </div>
        </div>
      </section>

      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <Metric
          label="اسناد قابل مشاهده"
          value={formatNumber(docTotal)}
          hint="در بایگانی شما"
          to="/search"
          tone="ink"
          icon={<DocumentStackIcon />}
          delay={40}
        />
        <Metric
          label="پوشه‌ها"
          value={formatNumber(folderTotal)}
          hint={`${formatNumber(roots.length)} پوشهٔ اصلی`}
          tone="amber"
          icon={<FolderOutlineIcon />}
          delay={80}
        />
        <Metric
          label="کارتابل"
          value={formatNumber(taskTotal)}
          hint={taskTotal > 0 ? 'نیازمند اقدام شما' : 'کاری در انتظار نیست'}
          to="/tasks"
          tone="emerald"
          icon={<InboxIcon />}
          delay={120}
        />
        <Metric
          label={quotaBytes > 0 ? 'فضای مصرف‌شده' : 'اشتراک دریافتی'}
          value={quotaBytes > 0 ? `${formatNumber(storagePct)}٪` : formatNumber(shareTotal)}
          hint={quotaBytes > 0 ? storageHint : recycle.data && recycle.data.total > 0 ? `${formatNumber(recycle.data.total)} سند در سطل بازیافت` : 'با شما به‌اشتراک گذاشته شده'}
          to={quotaBytes > 0 ? undefined : '/shared'}
          tone="sky"
          icon={quotaBytes > 0 ? <CloudIcon /> : <ShareIcon />}
          delay={160}
        />
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
        <div className="min-w-0 space-y-6">
          <FolderGrid
            categories={categories}
            roots={roots}
            countLabel={countLabel}
            onOpenDocuments={onOpenCategory}
            filingTarget={filingTarget}
          />

          <RecentDocs
            items={recentItems}
            loading={!demo && recent.isLoading}
            error={!demo ? recent.error : null}
            categoryNameOf={(id) => categories.find((category) => category.id === id)?.name}
          />
        </div>

        <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
          <UploadRail filingTarget={filingTarget} closed={fiscal.closed} />
          <TasksPanel tasks={demo ? [] : (tasks.data ?? [])} loading={!demo && tasks.isLoading} />

        </aside>
      </div>
    </div>
  );
}
