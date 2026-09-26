import { Alert, Button, Card, Chip, ProgressBar } from '../../components/ui';
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
    <div className="max-w-4xl space-y-5">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.searchAdmin}</h1>
      {status.isLoading && <ProgressBar className="rounded-full" />}
      {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
      {message && <Alert severity={message.severity}>{message.text}</Alert>}

      {data && (
        <Card>
          <div className="space-y-3">
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
                  <span dir="ltr" className="font-mono text-sm break-all text-ink-800">
                    {data.index ?? '—'}
                  </span>
                </Row>
                <Row label={t.indexedVersions}>
                  <span className="text-sm text-ink-800">{Math.max(data.indexedVersions, 0).toLocaleString('fa-IR')}</span>
                </Row>
              </>
            )}
            <Row label={t.extractions}>
              <span className="flex flex-wrap gap-1.5">
                {Object.entries(data.extractions).map(([key, count]) => (
                  <Chip
                    key={key}
                    size="small"
                    variant="outlined"
                    color={key === 'Failed' ? 'error' : 'default'}
                    label={`${extractionLabels[key] ?? key}: ${count.toLocaleString('fa-IR')}`}
                  />
                ))}
              </span>
            </Row>
          </div>
        </Card>
      )}

      <Card>
        <div className="space-y-3">
          <p className="text-sm text-paper-500">{t.reindexHelp}</p>
          <div className="flex flex-wrap gap-2">
            <Button
              disabled={busy || !data?.engineEnabled}
              onClick={() => run(async () => {
                await api.searchAdmin.reindex();
                return t.reindexQueued;
              })}
            >
              {t.reindex}
            </Button>
            <Button
              variant="outline"
              disabled={busy || failed === 0}
              onClick={() => run(async () => {
                const { queued } = await api.searchAdmin.retryFailed();
                return `${queued.toLocaleString('fa-IR')} ${t.retryQueued}`;
              })}
            >
              {t.retryFailed}
            </Button>
          </div>
        </div>
      </Card>
    </div>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-4">
      <span className="shrink-0 text-sm text-paper-500 sm:w-48">{label}</span>
      <div className="min-w-0">{children}</div>
    </div>
  );
}
