import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { DocumentList } from '../components/DocumentList';
import { Alert, Button, Card, PageHeader, Pagination, ProgressBar, Toast } from '../components/ui';
import { formatNumber } from '../lib/format';
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

  // Restoring the last document on the last page would otherwise leave an empty page.
  useEffect(() => {
    if (bin.data && !bin.isPlaceholderData && page > pages) setPage(pages);
  }, [bin.data, bin.isPlaceholderData, page, pages]);

  return (
    <div className="space-y-5">
      <PageHeader
        title={t.recycleBin}
        meta={
          bin.data && bin.data.total > 0 ? (
            <span className="rounded-full bg-paper-200/70 px-2.5 py-0.5 text-xs font-semibold text-paper-600 tabular-nums">
              {formatNumber(bin.data.total)} سند
            </span>
          ) : null
        }
        description="اسناد حذف‌شده تا پیش از پاک‌سازی نهایی اینجا می‌مانند و می‌توانید آن‌ها را بازگردانید."
      />
      {error && <Alert severity="error">{error}</Alert>}
      <Card flush className="overflow-hidden">
        {bin.isFetching && !bin.isLoading && <ProgressBar />}
        {bin.isLoading && <p className="py-14 text-center text-sm text-paper-500">در حال بارگذاری…</p>}
        {bin.isError ? (
          <div className="p-3 sm:p-4">
            <Alert severity="error">{describeError(bin.error)}</Alert>
          </div>
        ) : (
          bin.data && (
            <div className={bin.isPlaceholderData ? 'opacity-60 transition-opacity' : 'transition-opacity'}>
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
                      variant="outline"
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
        {bin.data && bin.data.total > 0 && (
          <div className="border-t border-paper-200 bg-paper-100/40 px-4 py-3 sm:px-5">
            <Pagination
              count={pages}
              page={page}
              total={bin.data.total}
              pageSize={bin.data.pageSize}
              disabled={bin.isFetching}
              onChange={(value) => {
                setPage(value);
                document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' });
              }}
            />
          </div>
        )}
      </Card>
      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} autoHideDuration={4000} />
    </div>
  );
}
