import { Alert, Button, Card, Pagination, ProgressBar, Switch, TextField } from '../components/ui';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { DocumentList } from '../components/DocumentList';
import { api } from '../lib/api';
import { formatNumber } from '../lib/format';
import { describeError, t } from '../strings';

const pageSize = 25;

export function BrowsePage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const categoryId = params.get('category');
  const page = Number(params.get('page') ?? '1');
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');
  const [includeSubcategories, setIncludeSubcategories] = useState(true);

  // Search as the user types, but not on every keystroke.
  useEffect(() => {
    const handle = setTimeout(() => {
      const next = new URLSearchParams(params);
      // Sent as typed: titles are stored as typed, and folding digits here would miss "۱۴۰۳".
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
  });

  const category = categories.data?.find((item) => item.id === categoryId);
  const heading = category?.name ?? t.allDocuments;
  const pages = documents.data ? Math.max(1, Math.ceil(documents.data.total / pageSize)) : 1;
  const filingTarget =
    category?.canCreate
      ? category.id
      : categories.data?.find((item) => item.canCreate)?.id;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">
          {heading}
          {documents.data && (
            <span className="ms-2 text-sm font-normal text-paper-400">({formatNumber(documents.data.total)})</span>
          )}
        </h1>
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
            {documents.data && (
              <DocumentList items={documents.data.items} onOpen={(item) => navigate(`/documents/${item.id}`)} />
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
