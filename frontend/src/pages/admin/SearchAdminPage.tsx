import { Alert, Button, Chip, LinearProgress, Paper, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { api } from '../../lib/api';
import { describeError, t } from '../../strings';

const extractionLabels: Record<string, string> = {
  Pending: 'در صف',
  Completed: 'انجام‌شده',
  Failed: 'ناموفق',
  Skipped: 'رد شده',
};

/** Indexing status, a full rebuild and a retry of failed text extractions (ADMIN_MANAGE_SEARCH). */
export function SearchAdminPage() {
  const status = useQuery({ queryKey: ['search-status'], queryFn: api.searchAdmin.status, refetchInterval: 15_000 });
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<string>) => {
    setBusy(true);
    setMessage(null);
    try {
      setMessage({ severity: 'success', text: await action() });
      await status.refetch();
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    } finally {
      setBusy(false);
    }
  };

  const data = status.data;
  const failed = data?.extractions.Failed ?? 0;

  return (
    <Stack spacing={2} sx={{ maxWidth: 900 }}>
      <Typography variant="h5" component="h1">
        {t.searchAdmin}
      </Typography>
      {status.isLoading && <LinearProgress />}
      {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
      {message && <Alert severity={message.severity}>{message.text}</Alert>}

      {data && (
        <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
          <Stack spacing={1.5}>
            <Row label={t.engine}>
              <Chip
                size="small"
                color={!data.engineEnabled ? 'default' : data.indexedVersions < 0 ? 'error' : 'success'}
                label={!data.engineEnabled ? t.disabled : data.indexedVersions < 0 ? t.unreachable : t.enabled}
              />
            </Row>
            <Row label={t.extractor}>
              <Chip size="small" color={data.extractorEnabled ? 'success' : 'default'} label={data.extractorEnabled ? t.enabled : t.disabled} />
            </Row>
            {data.engineEnabled && (
              <>
                <Row label={t.liveIndex}>
                  <Typography dir="ltr" variant="body2" sx={{ fontFamily: 'monospace' }}>
                    {data.index ?? '—'}
                  </Typography>
                </Row>
                <Row label={t.indexedVersions}>
                  <Typography variant="body2">{Math.max(data.indexedVersions, 0).toLocaleString('fa-IR')}</Typography>
                </Row>
              </>
            )}
            <Row label={t.extractions}>
              <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
                {Object.entries(data.extractions).map(([key, count]) => (
                  <Chip
                    key={key}
                    size="small"
                    variant="outlined"
                    color={key === 'Failed' ? 'error' : 'default'}
                    label={`${extractionLabels[key] ?? key}: ${count.toLocaleString('fa-IR')}`}
                  />
                ))}
              </Stack>
            </Row>
          </Stack>
        </Paper>
      )}

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Stack spacing={1.5}>
          <Typography variant="body2" color="text.secondary">
            {t.reindexHelp}
          </Typography>
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
            <Button
              variant="contained"
              disabled={busy || !data?.engineEnabled}
              onClick={() => run(async () => {
                await api.searchAdmin.reindex();
                return t.reindexQueued;
              })}
            >
              {t.reindex}
            </Button>
            <Button
              variant="outlined"
              disabled={busy || failed === 0}
              onClick={() => run(async () => {
                const { queued } = await api.searchAdmin.retryFailed();
                return `${queued.toLocaleString('fa-IR')} ${t.retryQueued}`;
              })}
            >
              {t.retryFailed}
            </Button>
          </Stack>
        </Stack>
      </Paper>
    </Stack>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 0.5, sm: 2 }} sx={{ alignItems: { sm: 'center' } }}>
      <Typography variant="body2" color="text.secondary" sx={{ width: { sm: 200 }, flexShrink: 0 }}>
        {label}
      </Typography>
      {children}
    </Stack>
  );
}
