import { Alert, Button, Card, Chip, ProgressBar, cx } from '../components/ui';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router';
import { FileTypeBadge } from '../components/FileTypeBadge';
import { FolderIcon } from '../components/FolderIcon';
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

function greetingForHour(hour: number): string {
  if (hour < 5) return 'شب‌بخیر';
  if (hour < 12) return 'صبح بخیر';
  if (hour < 17) return 'ظهر بخیر';
  if (hour < 21) return 'عصر بخیر';
  return 'شب‌بخیر';
}

function Metric({
  label,
  value,
  hint,
  to,
  accent,
  delay,
}: {
  label: string;
  value: string;
  hint?: string;
  to?: string;
  accent: string;
  delay: number;
}) {
  const body = (
    <>
      <div className={cx('mb-3 h-1 w-10 rounded-full', accent)} />
      <p className="text-xs font-medium text-paper-500">{label}</p>
      <p className="mt-1 text-2xl font-bold tracking-tight text-ink-900 tabular-nums">{value}</p>
      {hint && <p className="mt-1 text-[11px] text-paper-500">{hint}</p>}
    </>
  );

  const className = cx(
    'dashboard-stagger block rounded-2xl border border-paper-200/90 bg-paper-50/90 p-4 shadow-[var(--shadow-surface)] transition-all duration-200',
    to && 'hover:-translate-y-0.5 hover:border-ink-300 hover:shadow-[var(--shadow-elevated)]',
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
      style={{ animationDelay: `${80 + index * 40}ms` }}
      className="dashboard-stagger group flex flex-col overflow-hidden rounded-2xl border border-paper-200 bg-paper-50 transition-all duration-200 hover:-translate-y-0.5 hover:border-ink-300 hover:bg-ink-50/40 hover:shadow-[var(--shadow-elevated)]"
    >
      <button type="button" onClick={onOpen} className="flex flex-1 flex-col p-4 text-start">
        <FolderIcon className="mx-auto h-14 w-16 transition-transform duration-300 group-hover:scale-105" />
        <p className="mt-3 truncate text-sm font-semibold text-ink-900">{category.name}</p>
        <p className="mt-0.5 text-xs text-paper-500">{category.canCreate ? 'قابل ثبت سند' : 'فقط مشاهده'}</p>
        <div className="mt-3 flex items-center justify-between border-t border-paper-100 pt-3 text-xs text-paper-500">
          <span>{countLabel}</span>
          <span className="font-medium text-ink-600 opacity-0 transition-opacity group-hover:opacity-100">
            {hasChildren ? 'پوشه‌های داخل ←' : 'باز کردن ←'}
          </span>
        </div>
      </button>
      {hasChildren && (
        <button
          type="button"
          onClick={onOpenDocuments}
          className="border-t border-paper-100 px-4 py-2 text-start text-xs font-medium text-ink-600 hover:bg-ink-50/70"
        >
          مشاهده اسناد
        </button>
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
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          {trail.length > 0 && (
            <button
              type="button"
              onClick={() => setTrail((current) => current.slice(0, -1))}
              className="mb-1 text-xs text-ink-500 hover:text-ink-800"
            >
              ← بازگشت
            </button>
          )}
          <h2 className="text-xl font-bold text-ink-900">{focus?.name ?? 'پوشه‌های بایگانی'}</h2>
          <p className="mt-1 text-sm text-paper-500">
            {focus
              ? 'زیرپوشه‌ها به همین شکل نمایش داده می‌شوند. عدد هر کارت، تعداد فایل‌های داخل آن پوشه است.'
              : 'عدد روی هر پوشه، تعداد فایل‌های داخل آن است. اگر داخلش پوشه باشد، با کلیک همان‌طور باز می‌شود.'}
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
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
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
    <section className="dashboard-stagger rounded-2xl border border-paper-200/90 bg-paper-50/90 shadow-[var(--shadow-surface)]" style={{ animationDelay: '120ms' }}>
      <div className="flex items-center justify-between gap-3 border-b border-paper-100 px-4 py-3.5 sm:px-5">
        <div>
          <h2 className="text-base font-bold text-ink-900">اسناد اخیر</h2>
          <p className="text-xs text-paper-500">آخرین پرونده‌هایی که به آن‌ها دسترسی دارید</p>
        </div>
        <Button as={RouterLink} to="/search" size="sm" variant="ghost">
          همه
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
        <ul className="divide-y divide-paper-100">
          {items.map((item) => {
            const folder = categoryNameOf?.(item.categoryId)?.trim() || null;
            return (
              <li key={item.id}>
                <button
                  type="button"
                  onClick={() => navigate(`/documents/${item.id}`)}
                  className="flex w-full items-center gap-3 px-4 py-3 text-start transition-colors hover:bg-ink-50/60 sm:px-5"
                >
                  <FileTypeBadge fileName={item.fileName} mimeType={item.mimeType} className="size-10 rounded-xl" />
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
                    <p className="text-[11px] text-paper-500">{formatDateTime(item.updatedAt)}</p>
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
    <section className="dashboard-stagger rounded-2xl border border-paper-200/90 bg-paper-50/90 shadow-[var(--shadow-surface)]" style={{ animationDelay: '160ms' }}>
      <div className="flex items-center justify-between border-b border-paper-100 px-4 py-3">
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
        <ul className="divide-y divide-paper-100">
          {preview.map((task) => (
            <li key={task.id}>
              <RouterLink
                to="/tasks"
                className="block px-4 py-3 transition-colors hover:bg-ink-50/60"
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
      <div className="border-t border-paper-100 p-3">
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
          'border-dashed bg-gradient-to-b from-white to-ink-50/40 transition-colors',
          dragOver ? 'border-ink-400 bg-ink-50/70' : 'border-paper-300',
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
          <span className="mb-3 grid size-12 place-items-center rounded-2xl bg-ink-100 text-ink-600">
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
          <Button className="mt-3" size="sm" onClick={() => inputRef.current?.click()}>
            {t.chooseFile}
          </Button>
        </div>
      </Card>

      <Card flush className="overflow-hidden">
        <div className="flex items-center justify-between border-b border-paper-200 px-4 py-3">
          <p className="text-xs font-medium text-paper-500">بارگذاری‌های جاری</p>
          {busyCount > 0 && (
            <span className="text-[10px] font-semibold text-ink-600">{formatNumber(busyCount)} در حال ارسال</span>
          )}
        </div>
        {queue.length === 0 ? (
          <p className="px-4 py-7 text-center text-sm text-paper-500">فعلاً فایلی در صف نیست.</p>
        ) : (
          <ul className="max-h-64 divide-y divide-paper-200 overflow-y-auto">
            {queue.map((item) => (
              <li key={item.id} className="flex items-start gap-3 px-4 py-3">
                <FileTypeBadge fileName={item.file.name} mimeType={item.file.type} className="size-10 rounded-xl" />
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
                            item.status === 'done' ? 'bg-emerald-500' : 'bg-gradient-to-l from-copper-500 to-ink-500',
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
                  className="shrink-0 text-paper-400 hover:text-rose-600"
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

      {readyUploads.length > 0 ? (
        <Button fullWidth size="lg" onClick={() => registerUpload(readyUploads[0]!.upload!)}>
          ادامهٔ ثبت سند
        </Button>
      ) : (
        <Button as={RouterLink} to={newDocHref} fullWidth size="lg">
          {t.newDocument}
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
      <section className="relative overflow-hidden rounded-3xl border border-ink-200/60 bg-gradient-to-bl from-ink-900 via-ink-800 to-ink-700 text-white shadow-[var(--shadow-elevated)]">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 opacity-40"
          style={{
            backgroundImage:
              'radial-gradient(circle at 12% 20%, rgb(249 115 22 / 0.35), transparent 40%), radial-gradient(circle at 88% 10%, rgb(167 139 250 / 0.35), transparent 42%), linear-gradient(135deg, transparent 40%, rgb(255 255 255 / 0.04) 40%, rgb(255 255 255 / 0.04) 41%, transparent 41%)',
          }}
        />
        <div className="relative flex flex-col gap-5 p-5 sm:flex-row sm:items-end sm:justify-between sm:p-7">
          <div className="min-w-0">
            <p className="text-xs font-semibold tracking-[0.18em] text-ink-200 uppercase">Fillo · بایگانی اسناد</p>
            <h1 className="mt-2 text-2xl font-bold tracking-tight sm:text-3xl">
              {hello}، {displayName}
            </h1>
            <p className="mt-2 max-w-xl text-sm leading-6 text-ink-100/90">
              داشبورد مدیریت خودکار — وضعیت بایگانی، کارتابل و بارگذاری امروز را یک‌جا ببینید.
            </p>
            <p className="mt-3 text-xs text-ink-200/80">{today}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button as={RouterLink} to={filingTarget ? `/new?category=${filingTarget}` : '/new'} size="lg">
              {t.newDocument}
            </Button>
            <Button as={RouterLink} to="/search" size="lg" variant="outline" className="border-white/30 bg-white/10 text-white hover:bg-white/20">
              {t.searchEverything}
            </Button>
            <Button as={RouterLink} to="/help" size="lg" variant="ghost" className="text-ink-100 hover:bg-white/10 hover:text-white">
              {t.userGuideShort}
            </Button>
          </div>
        </div>
        {quotaBytes > 0 && (
          <div className="relative border-t border-white/10 px-5 py-3 sm:px-7">
            <div className="mb-1.5 flex items-center justify-between text-[11px] text-ink-100/80">
              <span>فضای ذخیره‌سازی</span>
              <span>
                {formatNumber(storagePct)}٪ · {storageHint}
              </span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-white/15">
              <div
                className="h-full rounded-full bg-gradient-to-l from-copper-400 to-ink-200 transition-[width] duration-700"
                style={{ width: `${storagePct}%` }}
              />
            </div>
          </div>
        )}
      </section>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Metric
          label="اسناد قابل مشاهده"
          value={formatNumber(docTotal)}
          hint="در بایگانی شما"
          to="/search"
          accent="bg-ink-500"
          delay={40}
        />
        <Metric
          label="پوشه‌ها"
          value={formatNumber(folderTotal)}
          hint={`${formatNumber(roots.length)} پوشهٔ اصلی`}
          accent="bg-copper-500"
          delay={80}
        />
        <Metric
          label="کارتابل"
          value={formatNumber(taskTotal)}
          hint={taskTotal > 0 ? 'نیازمند اقدام' : 'خالی'}
          to="/tasks"
          accent="bg-emerald-500"
          delay={120}
        />
        <Metric
          label="اشتراک دریافتی"
          value={formatNumber(shareTotal)}
          hint={recycle.data && recycle.data.total > 0 ? `${formatNumber(recycle.data.total)} در سطل` : 'با شما به‌اشتراک گذاشته'}
          to="/shared"
          accent="bg-sky-500"
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

        <aside className="space-y-4 xl:sticky xl:top-20 xl:self-start">
          <UploadRail filingTarget={filingTarget} closed={fiscal.closed} />
          <TasksPanel tasks={demo ? [] : (tasks.data ?? [])} loading={!demo && tasks.isLoading} />

          <section
            className="dashboard-stagger rounded-2xl border border-paper-200/90 bg-paper-50/90 p-4 shadow-[var(--shadow-surface)]"
            style={{ animationDelay: '200ms' }}
          >
            <p className="text-xs font-semibold tracking-wide text-paper-500 uppercase">میان‌برها</p>
            <div className="mt-3 grid grid-cols-2 gap-2">
              <Button as={RouterLink} to="/shared" size="sm" variant="outline" fullWidth>
                اشتراک‌ها
              </Button>
              <Button as={RouterLink} to="/recycle-bin" size="sm" variant="outline" fullWidth>
                {t.recycleBin}
              </Button>
              <Button as={RouterLink} to="/help" size="sm" variant="outline" fullWidth>
                {t.userGuideShort}
              </Button>
              <Button as={RouterLink} to="/search" size="sm" variant="outline" fullWidth>
                جستجو
              </Button>
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
}
