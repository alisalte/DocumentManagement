import { Alert, Button, LinearProgress, Pagination, Paper, Snackbar, Stack, Typography } from '@mui/material';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { DocumentList } from '../components/DocumentList';
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
    <Stack spacing={2}>
      <Typography variant="h5" component="h1">
        {t.recycleBin}
      </Typography>
      {error && <Alert severity="error">{error}</Alert>}
      <Paper variant="outlined" sx={{ p: { xs: 1, sm: 0 } }}>
        {bin.isFetching && <LinearProgress />}
        {bin.isError ? (
          <Alert severity="error">{describeError(bin.error)}</Alert>
        ) : (
          bin.data && (
            <DocumentList
              items={bin.data.items}
              dateOf={(item) => item.deletedAt ?? item.updatedAt}
              dateLabel={t.deletedAt}
              renderAction={(item) => (
                <Button size="small" onClick={() => restore(item.id)}>
                  {t.restore}
                </Button>
              )}
            />
          )
        )}
      </Paper>
      {pages > 1 && <Pagination sx={{ alignSelf: 'center' }} count={pages} page={page} onChange={(_, value) => setPage(value)} />}
      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
  );
}
