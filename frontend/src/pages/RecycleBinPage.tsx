import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { DocumentList } from '../components/DocumentList';
import { Alert, Button, Card, Pagination, ProgressBar, Toast } from '../components/ui';
import { api } from '../lib/api';
import { useFiscalYear } from '../lib/fiscalYear';
import { describeError, t } from '../strings';

/** Deleted documents the user may restore. Nothing here has left storage yet. */
export function RecycleBinPage() {
  const queryClient = useQueryClient();
  const fiscal = useFiscalYear();
  const [page, setPage] = useState(1);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const bin = useQuery({
    queryKey: ['recycle-bin', page],
    queryFn: () => api.recycleBin(page),
    placeholderData: keepPreviousData,
  });

  const restore = async (id: string) => {
    setError(null);
    try {
      await api.restoreDocument(id);
      setNotice(t.restored);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['recycle-bin'] }),
        queryClient.invalidateQueries({ queryKey: ['documents'] }),
      ]);
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  const pages = bin.data ? Math.max(1, Math.ceil(bin.data.total / bin.data.pageSize)) : 1;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.recycleBin}</h1>
      </div>
      {error && <Alert severity="error">{error}</Alert>}
      <Card flush className="overflow-hidden">
        {bin.isFetching && <ProgressBar />}
        {bin.isError ? (
          <div className="p-3 sm:p-4">
            <Alert severity="error">{describeError(bin.error)}</Alert>
          </div>
        ) : (
          bin.data && (
            <div className="p-2 sm:p-0">
              <DocumentList
                items={bin.data.items}
                categoryNameOf={(id) => categories.data?.find((category) => category.id === id)?.name}
                dateOf={(item) => item.deletedAt ?? item.updatedAt}
                dateLabel={t.deletedAt}
                emptyHint="سندی در سطل بازیافت نیست."
                renderAction={(item) => {
                  const closed = fiscal.currentYear > 0 && item.fiscalYear !== fiscal.currentYear;
                  return (
                    <Button
                      size="sm"
                      variant="ghost"
                      disabled={closed}
                      title={closed ? t.fiscalYearClosedBanner : undefined}
                      onClick={() => restore(item.id)}
                    >
                      {t.restore}
                    </Button>
                  );
                }}
              />
            </div>
          )
        )}
      </Card>
      {pages > 1 && <Pagination count={pages} page={page} onChange={(value) => setPage(value)} />}
      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} autoHideDuration={4000} />
    </div>
  );
}
