import { Alert, Button, Card, Pagination, ProgressBar, Select, Switch, TextField, cx } from '../components/ui';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { DocumentList } from '../components/DocumentList';
import { FolderIcon } from '../components/FolderIcon';
import { api, type CategoryNode } from '../lib/api';
import { formatBytes, formatNumber } from '../lib/format';
import { describeError, t } from '../strings';

const pageSize = 25;

const demoUploads = [
  { name: 'visual_brand_identity.pdf', size: 2_800_000, progress: 28 },
  { name: 'marketing_assets.zip', size: 45_000_000, progress: 95 },
  { name: 'annual_report.docx', size: 1_200_000, progress: 67 },
  { name: 'logo_variations.svg', size: 890_000, progress: 42 },
  { name: 'product_photos.jpg', size: 3_400_000, progress: 81 },
];

function fileTypeIcon(name: string) {
  const ext = name.split('.').pop()?.toLowerCase();
  const color =
    ext === 'pdf' ? 'text-rose-400' : ext === 'docx' || ext === 'doc' ? 'text-sky-400' : ext === 'svg' ? 'text-violet-400' : 'text-amber-400';
  return (
    <span className={cx('grid size-9 shrink-0 place-items-center rounded-lg bg-paper-300/50 text-xs font-bold uppercase', color)}>
      {ext?.slice(0, 3) ?? 'file'}
    </span>
  );
}

function countDescendants(categories: CategoryNode[], rootId: string): number {
  let n = 0;
  const stack = categories.filter((c) => c.parentId === rootId).map((c) => c.id);
  while (stack.length) {
    const id = stack.pop()!;
    n += 1;
    for (const c of categories) {
      if (c.parentId === id) stack.push(c.id);
    }
  }
  return n;
}

function FolderCard({
  category,
  childCount,
  onOpen,
}: {
  category: CategoryNode;
  childCount: number;
  onOpen: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onOpen}
      className="group flex flex-col rounded-2xl border border-paper-300/60 bg-paper-200/70 p-4 text-start transition-all hover:border-ink-500/30 hover:bg-paper-300/40 hover:shadow-[0_8px_24px_rgb(0_0_0/0.25)]"
    >
      <FolderIcon className="mx-auto h-16 w-20 transition-transform group-hover:scale-105" />
      <p className="mt-3 truncate text-sm font-semibold text-ink-900">{category.name}</p>
      <p className="mt-0.5 text-xs text-paper-500">پوشه سازمانی</p>
      <div className="mt-4 flex items-end justify-between gap-2 border-t border-paper-300/50 pt-3">
        <div>
          <p className="text-[10px] text-paper-500">تعداد زیرپوشه</p>
          <p className="text-sm font-medium text-ink-900">{formatNumber(childCount)}</p>
        </div>
        <div className="text-end">
          <p className="text-[10px] text-paper-500">مرتب‌سازی</p>
          <p className="text-xs text-paper-600">نوع کاربرد</p>
        </div>
      </div>
    </button>
  );
}

function AutoManageHome({
  categories,
  filingTarget,
  onOpenCategory,
}: {
  categories: CategoryNode[];
  filingTarget: string | undefined;
  onOpenCategory: (id: string) => void;
}) {
  const roots = useMemo(
    () => categories.filter((c) => c.parentId === null && c.canView).sort((a, b) => a.sortOrder - b.sortOrder),
    [categories],
  );

  return (
    <div className="flex flex-col gap-6 xl:flex-row xl:items-start">
      <div className="w-full shrink-0 space-y-4 xl:w-[320px]">
        <Card className="border-dashed border-paper-400/50 bg-paper-300/20">
          <div className="flex flex-col items-center py-4 text-center">
            <span className="mb-3 grid size-14 place-items-center rounded-2xl bg-paper-300/50 text-paper-500">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} className="size-8">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 16.5V9.75m0 0 3 3m-3-3-3 3M6.75 19.5a4.5 4.5 0 0 1-1.41-8.775 5.25 5.25 0 0 1 10.233-2.33 4.5 4.5 0 0 1 4.5 4.5v.008H19.5a3 3 0 0 1 3 3v1.5a3 3 0 0 1-3 3H6.75Z" />
              </svg>
            </span>
            <p className="text-sm font-medium text-ink-900">بارگذاری فایل</p>
            <p className="mt-1 max-w-[16rem] text-xs leading-5 text-paper-500">
              فایل‌ها را اینجا رها کنید تا در پوشه‌های مناسب مرتب شوند.
            </p>
            <Button as={RouterLink} to={filingTarget ? `/new?category=${filingTarget}` : '/new'} className="mt-4" size="sm">
              {t.chooseFile}
            </Button>
          </div>
        </Card>

        <Card flush className="overflow-hidden">
          <p className="border-b border-paper-300/50 px-4 py-3 text-xs font-medium text-paper-500">بارگذاری‌های جاری</p>
          <ul className="divide-y divide-paper-300/40">
            {demoUploads.map((file) => (
              <li key={file.name} className="flex items-start gap-3 px-4 py-3">
                {fileTypeIcon(file.name)}
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm text-ink-900" dir="ltr">
                    {file.name}
                  </p>
                  <p className="text-xs text-paper-500">{formatBytes(file.size)}</p>
                  <div className="mt-2 h-1 overflow-hidden rounded-full bg-paper-400/40">
                    <div
                      className="h-full rounded-full bg-gradient-to-l from-copper-500 to-ink-500"
                      style={{ width: `${file.progress}%` }}
                    />
                  </div>
                  <p className="mt-1 text-[10px] text-paper-500">{formatNumber(file.progress)}٪</p>
                </div>
                <button type="button" className="shrink-0 text-paper-500 hover:text-rose-400" aria-label="حذف">
                  <svg viewBox="0 0 20 20" fill="currentColor" className="size-4">
                    <path
                      fillRule="evenodd"
                      d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z"
                      clipRule="evenodd"
                    />
                  </svg>
                </button>
              </li>
            ))}
          </ul>
        </Card>

        <div className="space-y-3">
          <Select label="مرتب‌سازی بر اساس" size="sm" defaultValue="ai">
            <option value="ai">مرتب‌سازی خودکار با هوش مصنوعی</option>
            <option value="type">نوع فایل</option>
            <option value="date">تاریخ</option>
          </Select>
          <Button fullWidth size="lg">
            سازماندهی
          </Button>
        </div>
      </div>

      <div className="min-w-0 flex-1">
        <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-xl font-bold text-ink-900">پوشه‌های مرتب‌شده</h2>
            <p className="mt-1 text-sm text-paper-500">پوشه‌های سطح اول را برای مشاهده اسناد باز کنید.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => window.location.reload()}>
              بروزرسانی
            </Button>
            <Button variant="outline" size="sm">
              مرتب‌سازی
            </Button>
            {filingTarget && (
              <Button as={RouterLink} to={`/?category=${filingTarget}`} size="sm">
                رفتن به «پرونده‌های من»
              </Button>
            )}
          </div>
        </div>

        {roots.length === 0 ? (
          <Card>
            <p className="py-8 text-center text-sm text-paper-500">پوشه‌ای برای نمایش نیست.</p>
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
            {roots.map((cat) => (
              <FolderCard
                key={cat.id}
                category={cat}
                childCount={countDescendants(categories, cat.id)}
                onOpen={() => onOpenCategory(cat.id)}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export function BrowsePage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const categoryId = params.get('category');
  const page = Number(params.get('page') ?? '1');
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');
  const [includeSubcategories, setIncludeSubcategories] = useState(true);

  useEffect(() => {
    const handle = setTimeout(() => {
      const next = new URLSearchParams(params);
      const value = searchInput.trim();
      if (value) next.set('q', value);
      else next.delete('q');
      next.delete('page');
      if (next.toString() !== params.toString()) setParams(next, { replace: true });
    }, 350);
    return () => clearTimeout(handle);
  }, [searchInput, params, setParams]);

  const search = params.get('q') ?? '';
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const documents = useQuery({
    queryKey: ['documents', categoryId, includeSubcategories, search, page],
    queryFn: () => api.documents({ categoryId, includeSubcategories, search, page, pageSize }),
    placeholderData: keepPreviousData,
    enabled: !!(categoryId || search),
  });

  const category = categories.data?.find((item) => item.id === categoryId);
  const heading = category?.name ?? t.allDocuments;
  const pages = documents.data ? Math.max(1, Math.ceil(documents.data.total / pageSize)) : 1;
  const filingTarget =
    category?.canCreate ? category.id : categories.data?.find((item) => item.canCreate)?.id;

  const showAutoHome = !categoryId && !search && categories.data;

  if (showAutoHome) {
    return (
      <AutoManageHome
        categories={categories.data ?? []}
        filingTarget={filingTarget}
        onOpenCategory={(id) => {
          const next = new URLSearchParams(params);
          next.set('category', id);
          setParams(next);
        }}
      />
    );
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <button
            type="button"
            onClick={() => {
              const next = new URLSearchParams(params);
              next.delete('category');
              next.delete('q');
              next.delete('page');
              setParams(next);
            }}
            className="mb-1 text-xs text-ink-500 hover:text-ink-800"
          >
            ← بازگشت به مدیریت خودکار
          </button>
          <h1 className="text-2xl font-bold tracking-tight text-ink-900">
            {heading}
            {documents.data && (
              <span className="ms-2 text-sm font-normal text-paper-500">({formatNumber(documents.data.total)})</span>
            )}
          </h1>
        </div>
        {filingTarget && (
          <Button as={RouterLink} to={`/new?category=${filingTarget}`}>
            {t.newDocument}
          </Button>
        )}
      </div>

      <Card>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <TextField
            label={t.search}
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            size="sm"
            type="search"
            className="flex-1"
          />
          {categoryId && (
            <div className="shrink-0">
              <Switch
                label={t.includeSubcategories}
                checked={includeSubcategories}
                onChange={(event) => setIncludeSubcategories(event.target.checked)}
              />
            </div>
          )}
        </div>
      </Card>

      <Card flush>
        {documents.isFetching && <ProgressBar />}
        {documents.isError ? (
          <div className="p-3 sm:p-4">
            <Alert severity="error" action={<Button size="sm" onClick={() => documents.refetch()}>{t.retry}</Button>}>
              {describeError(documents.error)}
            </Alert>
          </div>
        ) : (
          <div className="p-2 sm:p-0">
            {documents.data && documents.data.items.length === 0 ? (
              <div className="space-y-3 px-4 py-10 text-center">
                <p className="text-sm text-paper-500">{t.noDocuments}</p>
                <Button as={RouterLink} to="/help" variant="outline" size="sm">
                  {t.userGuide}
                </Button>
              </div>
            ) : (
              documents.data && (
                <DocumentList items={documents.data.items} onOpen={(item) => navigate(`/documents/${item.id}`)} />
              )
            )}
          </div>
        )}
      </Card>

      {pages > 1 && (
        <Pagination
          count={pages}
          page={page}
          onChange={(value) => {
            const next = new URLSearchParams(params);
            next.set('page', String(value));
            setParams(next);
          }}
        />
      )}
    </div>
  );
}
