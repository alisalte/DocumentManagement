import { Alert, Button, Card, PageHeader, Pagination, ProgressBar, Switch, cx } from '../components/ui';
import { PlusIcon, SearchIcon } from '../components/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { DocumentList } from '../components/DocumentList';
import { api, type CategoryNode, type DocumentListItem } from '../lib/api';
import { useFiscalYear } from '../lib/fiscalYear';
import { formatNumber } from '../lib/format';
import { describeError, t } from '../strings';
import { DashboardHome } from './DashboardHome';

const pageSize = 25;

export function BrowsePage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const categoryId = params.get('category');
  const requestedPage = Number(params.get('page') ?? '1');
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const search = params.get('q') ?? '';
  const [searchInput, setSearchInput] = useState(search);
  const [includeSubcategories, setIncludeSubcategories] = useState(true);

  // Another folder or a link can change the query; the box follows it.
  useEffect(() => {
    setSearchInput(search);
  }, [search]);

  // Typing filters after a pause. Only a changed query goes back to page 1: comparing the whole
  // URL here used to drop ?page= every time, so the list snapped back to the first page.
  useEffect(() => {
    const value = searchInput.trim();
    if (value === search) return;
    const handle = setTimeout(() => {
      const next = new URLSearchParams(params);
      if (value) next.set('q', value);
      else next.delete('q');
      next.delete('page');
      setParams(next, { replace: true });
    }, 350);
    return () => clearTimeout(handle);
  }, [searchInput, search, params, setParams]);

  const isDemo = import.meta.env.DEV && params.get('demo') === '1';
  const fiscal = useFiscalYear();
  const fiscalYear = !isDemo && fiscal.ready && fiscal.selectedYear > 0 ? fiscal.selectedYear : undefined;
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories, enabled: !isDemo });
  const documents = useQuery({
    queryKey: ['documents', categoryId, includeSubcategories, search, page, fiscalYear ?? null],
    queryFn: () => api.documents({ categoryId, includeSubcategories, search, page, pageSize, fiscalYear }),
    placeholderData: keepPreviousData,
    enabled: !isDemo && !!(categoryId || search) && (fiscal.ready || fiscal.failed),
  });

  const demoFolders: CategoryNode[] = [
    { id: 'root', parentId: null, name: 'اسناد', code: 'ROOT', description: 'ریشه‌ی بایگانی', depth: 0, isActive: true, sortOrder: 0, canView: true, canCreate: false },
    { id: 'd1', parentId: 'root', name: 'ویدیوهای پژوهش کاربر', code: 'ur', description: null, depth: 1, isActive: true, sortOrder: 1, canView: true, canCreate: true },
    { id: 'd1a', parentId: 'd1', name: 'مصاحبه‌ها', code: 'ur-int', description: null, depth: 2, isActive: true, sortOrder: 1, canView: true, canCreate: true },
    { id: 'd2', parentId: 'root', name: 'کتابخانه کامپوننت UI', code: 'ui', description: null, depth: 1, isActive: true, sortOrder: 2, canView: true, canCreate: true },
    { id: 'd3', parentId: 'root', name: 'دارایی‌های برند', code: 'br', description: null, depth: 1, isActive: true, sortOrder: 3, canView: true, canCreate: true },
    { id: 'd4', parentId: 'root', name: 'مستندات محصول', code: 'pd', description: null, depth: 1, isActive: true, sortOrder: 4, canView: true, canCreate: true },
    { id: 'd5', parentId: 'root', name: 'کمپین‌های بازاریابی', code: 'mk', description: null, depth: 1, isActive: true, sortOrder: 5, canView: true, canCreate: true },
    { id: 'd6', parentId: 'root', name: 'طراحی‌های فیگما', code: 'fg', description: null, depth: 1, isActive: true, sortOrder: 6, canView: true, canCreate: true },
    { id: 'd7', parentId: 'root', name: 'گزارش‌های فصلی', code: 'qr', description: null, depth: 1, isActive: true, sortOrder: 7, canView: true, canCreate: true },
    { id: 'd8', parentId: 'root', name: 'قراردادها و حقوقی', code: 'lg', description: null, depth: 1, isActive: true, sortOrder: 8, canView: true, canCreate: true },
  ];
  const demoDocs: DocumentListItem[] = [
    {
      id: 'demo-1',
      title: 'قرارداد خدمات ۱۴۰۴',
      categoryId: 'd1a',
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
      categoryId: 'd7',
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
    {
      id: 'demo-3',
      title: 'اسلاید معرفی محصول',
      categoryId: 'd4',
      documentTypeId: 't',
      ownerId: 'u',
      currentVersionLabel: 'V1.0',
      fileName: 'pitch.pptx',
      mimeType: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      fileSize: 1_500_000,
      updatedAt: new Date(Date.now() - 172_800_000).toISOString(),
      deletedAt: null,
      deleteReason: null,
      fiscalYear: 1405,
    },
  ];

  const folderSource = categories.data?.length ? categories.data : isDemo ? demoFolders : undefined;
  const category = folderSource?.find((item) => item.id === categoryId);
  const heading = category?.name ?? t.allDocuments;
  const listItems = isDemo
    ? demoDocs.filter((item) => {
        if (search && !item.title.includes(search) && !(item.fileName ?? '').includes(search)) return false;
        if (!categoryId) return true;
        if (!includeSubcategories) return item.categoryId === categoryId;
        const allowed = new Set<string>([categoryId]);
        for (const folder of folderSource ?? []) {
          if (folder.parentId && allowed.has(folder.parentId)) allowed.add(folder.id);
        }
        return allowed.has(item.categoryId);
      })
    : (documents.data?.items ?? []);
  const total = isDemo ? listItems.length : (documents.data?.total ?? 0);
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const filingTarget =
    category?.canCreate ? category.id : folderSource?.find((item) => item.canCreate)?.id;
  const showDashboard = !categoryId && !search && folderSource;
  const categoryNameOf = (id: string) => folderSource?.find((folder) => folder.id === id)?.name;

  // A page past the end (a bookmark, or files moved away) shows the last page instead of nothing.
  const lastPage = documents.data && !documents.isPlaceholderData ? pages : null;
  useEffect(() => {
    if (lastPage !== null && page > lastPage) {
      const next = new URLSearchParams(params);
      if (lastPage > 1) next.set('page', String(lastPage));
      else next.delete('page');
      setParams(next, { replace: true });
    }
  }, [lastPage, page, params, setParams]);

  if (showDashboard) {
    return (
      <DashboardHome
        categories={folderSource ?? []}
        filingTarget={filingTarget ?? (isDemo ? 'd1' : undefined)}
        demo={isDemo}
        directFileCounts={
          isDemo
            ? demoDocs.reduce<Record<string, number>>((counts, item) => {
                counts[item.categoryId] = (counts[item.categoryId] ?? 0) + 1;
                return counts;
              }, {})
            : undefined
        }
        onOpenCategory={(id) => {
          const next = new URLSearchParams(params);
          next.set('category', id);
          if (isDemo) next.set('demo', '1');
          setParams(next);
        }}
      />
    );
  }

  const goToPage = (value: number) => {
    const next = new URLSearchParams(params);
    if (value > 1) next.set('page', String(value));
    else next.delete('page');
    setParams(next);
    document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const backToDashboard = () => {
    const next = new URLSearchParams(params);
    next.delete('category');
    next.delete('q');
    next.delete('page');
    setParams(next);
  };

  const trail = category ? ancestorTrail(folderSource ?? [], category.id) : [];
  const folderLink = (id: string) => (isDemo ? `/?category=${id}&demo=1` : `/?category=${id}`);
  const loading = !isDemo && documents.isLoading;

  return (
    <div className="space-y-5">
      <PageHeader
        breadcrumbs={[
          { label: 'مدیریت خودکار', onClick: backToDashboard },
          ...trail.map((folder) => ({ label: folder.name, to: folderLink(folder.id) })),
          ...(category ? [{ label: category.name }] : search ? [{ label: t.search }] : []),
        ]}
        title={heading}
        meta={
          <span className="rounded-full bg-paper-200/70 px-2.5 py-0.5 text-xs font-semibold text-paper-600 tabular-nums">
            {formatNumber(total)} سند
          </span>
        }
        actions={
          filingTarget && (
            <Button as={RouterLink} to={`/new?category=${filingTarget}`}>
              <PlusIcon strokeWidth={2.2} />
              {t.newDocument}
            </Button>
          )
        }
      />

      <Card flush className="overflow-hidden">
        <div className="flex flex-col gap-3 border-b border-paper-200 p-3 sm:flex-row sm:items-center sm:p-4">
          <div className="relative flex-1">
            <SearchIcon className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-paper-400" />
            <input
              type="search"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              placeholder={category ? `${t.search} «${category.name}»…` : `${t.search}…`}
              aria-label={t.search}
              className="h-10 w-full rounded-xl border border-paper-300 bg-paper-50 ps-9 pe-3 text-sm text-ink-900 shadow-[0_1px_2px_rgb(16_24_40/0.05)] transition-[border-color,box-shadow] placeholder:text-paper-400 hover:border-paper-400 focus:border-ink-500 focus:outline-none focus:ring-4 focus:ring-ink-500/15"
            />
          </div>
          {categoryId && (
            <div className="shrink-0 rounded-xl border border-paper-200 bg-paper-100/60 px-3 py-2">
              <Switch
                label={t.includeSubcategories}
                checked={includeSubcategories}
                onChange={(event) => {
                  setIncludeSubcategories(event.target.checked);
                  if (params.has('page')) {
                    const next = new URLSearchParams(params);
                    next.delete('page');
                    setParams(next, { replace: true });
                  }
                }}
              />
            </div>
          )}
        </div>

        {!isDemo && documents.isFetching && !loading && <ProgressBar />}
        {!isDemo && documents.isError ? (
          <div className="p-4">
            <Alert severity="error" action={<Button size="sm" variant="outline" onClick={() => documents.refetch()}>{t.retry}</Button>}>
              {describeError(documents.error)}
            </Alert>
          </div>
        ) : loading ? (
          <ListSkeleton />
        ) : (
          <div className={cx('transition-opacity', !isDemo && documents.isPlaceholderData && 'opacity-60')}>
            <DocumentList
              items={listItems}
              categoryNameOf={categoryNameOf}
              onOpen={(item) => navigate(`/documents/${item.id}`)}
              emptyHint={
                search
                  ? 'عبارت دیگری را امتحان کنید یا جستجو را پاک کنید.'
                  : 'هنوز سندی در این پوشه ثبت نشده است.'
              }
              emptyAction={
                search ? (
                  <Button variant="outline" size="sm" onClick={() => setSearchInput('')}>
                    پاک کردن جستجو
                  </Button>
                ) : filingTarget ? (
                  <Button as={RouterLink} to={`/new?category=${filingTarget}`} size="sm">
                    <PlusIcon strokeWidth={2.2} />
                    {t.newDocument}
                  </Button>
                ) : (
                  <Button as={RouterLink} to="/help" variant="outline" size="sm">
                    {t.userGuide}
                  </Button>
                )
              }
            />
          </div>
        )}

        {total > 0 && (
          <div className="border-t border-paper-200 bg-paper-100/40 px-4 py-3 sm:px-5">
            <Pagination
              count={pages}
              page={Math.min(page, pages)}
              total={total}
              pageSize={pageSize}
              disabled={!isDemo && documents.isFetching}
              onChange={goToPage}
            />
          </div>
        )}
      </Card>
    </div>
  );
}

/** Folders above this one, root first, so the header can show the path. */
function ancestorTrail(categories: CategoryNode[], id: string): CategoryNode[] {
  const byId = new Map(categories.map((folder) => [folder.id, folder]));
  const trail: CategoryNode[] = [];
  let current = byId.get(id);
  while (current?.parentId) {
    const parent = byId.get(current.parentId);
    if (!parent) break;
    trail.unshift(parent);
    current = parent;
  }
  return trail;
}

function ListSkeleton() {
  return (
    <div className="divide-y divide-paper-200/70" aria-hidden>
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className="flex items-center gap-3 px-5 py-4">
          <span className="size-9 shrink-0 animate-pulse rounded-lg bg-paper-200" />
          <div className="flex-1 space-y-2">
            <span className="block h-3 w-2/5 animate-pulse rounded bg-paper-200" />
            <span className="block h-2.5 w-1/4 animate-pulse rounded bg-paper-200/70" />
          </div>
          <span className="hidden h-3 w-24 animate-pulse rounded bg-paper-200/70 md:block" />
        </div>
      ))}
    </div>
  );
}
