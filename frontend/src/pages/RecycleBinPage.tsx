import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { DocumentList } from '../components/DocumentList';
import { Alert, Button, Card, Pagination, ProgressBar, Toast } from '../components/ui';
import { api } from '../lib/api';
import { describeError, t } from '../strings';

/** Deleted documents the user may restore. Nothing here has left storage yet. */
export function RecycleBinPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

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
                dateOf={(item) => item.deletedAt ?? item.updatedAt}
                dateLabel={t.deletedAt}
                renderAction={(item) => (
                  <Button size="sm" variant="ghost" onClick={() => restore(item.id)}>
                    {t.restore}
                  </Button>
                )}
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
