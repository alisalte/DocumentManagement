import { Alert, Button, Card, Pagination, ProgressBar, Switch, TextField } from '../components/ui';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { DocumentList } from '../components/DocumentList';
import { api, type CategoryNode } from '../lib/api';
import { formatNumber } from '../lib/format';
import { describeError, t } from '../strings';
import { DashboardHome } from './DashboardHome';

const pageSize = 25;

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

  const demoFolders: CategoryNode[] = [
    { id: 'd1', parentId: null, name: 'ویدیوهای پژوهش کاربر', code: 'ur', description: null, depth: 0, isActive: true, sortOrder: 1, canView: true, canCreate: true },
    { id: 'd2', parentId: null, name: 'کتابخانه کامپوننت UI', code: 'ui', description: null, depth: 0, isActive: true, sortOrder: 2, canView: true, canCreate: true },
    { id: 'd3', parentId: null, name: 'دارایی‌های برند', code: 'br', description: null, depth: 0, isActive: true, sortOrder: 3, canView: true, canCreate: true },
    { id: 'd4', parentId: null, name: 'مستندات محصول', code: 'pd', description: null, depth: 0, isActive: true, sortOrder: 4, canView: true, canCreate: true },
    { id: 'd5', parentId: null, name: 'کمپین‌های بازاریابی', code: 'mk', description: null, depth: 0, isActive: true, sortOrder: 5, canView: true, canCreate: true },
    { id: 'd6', parentId: null, name: 'طراحی‌های فیگما', code: 'fg', description: null, depth: 0, isActive: true, sortOrder: 6, canView: true, canCreate: true },
    { id: 'd7', parentId: null, name: 'گزارش‌های فصلی', code: 'qr', description: null, depth: 0, isActive: true, sortOrder: 7, canView: true, canCreate: true },
    { id: 'd8', parentId: null, name: 'قراردادها و حقوقی', code: 'lg', description: null, depth: 0, isActive: true, sortOrder: 8, canView: true, canCreate: true },
  ];
  const isDemo = import.meta.env.DEV && params.get('demo') === '1';
  const folderSource = categories.data?.length ? categories.data : isDemo ? demoFolders : undefined;
  const showDashboard = !categoryId && !search && folderSource;

  if (showDashboard) {
    return (
      <DashboardHome
        categories={folderSource ?? []}
        filingTarget={filingTarget ?? (isDemo ? 'd1' : undefined)}
        demo={isDemo}
        onOpenCategory={(id) => {
          const next = new URLSearchParams(params);
          next.set('category', id);
          if (isDemo) next.set('demo', '1');
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
                <DocumentList
                  items={documents.data.items}
                  categoryNameOf={(id) => categories.data?.find((category) => category.id === id)?.name}
                  onOpen={(item) => navigate(`/documents/${item.id}`)}
                />
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
