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
  const engineMode = data?.engineMode ?? (data?.engineEnabled ? 'OpenSearch' : 'Disabled');
  const engineChip =
    engineMode === 'Disabled' || !data?.engineEnabled
      ? { color: 'default' as const, label: t.disabled }
      : engineMode === 'Unreachable' || (data?.indexedVersions ?? 0) < 0
        ? { color: 'error' as const, label: t.unreachable }
        : { color: 'success' as const, label: t.enabled };

  const extractorMode = data?.extractorMode;
  const extractorHelp =
    !data?.extractorEnabled
      ? t.extractorDisabledHelp
      : extractorMode === 'Tika'
        ? t.extractorTikaHelp
        : extractorMode === 'LocalTesseract'
          ? t.extractorLocalHelp
          : null;

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
              <Chip size="small" color={engineChip.color} label={engineChip.label} />
            </Row>
            {(engineMode === 'Disabled' || !data.engineEnabled) && (
              <Alert severity="info">{t.engineDisabledHelp}</Alert>
            )}
            {engineMode === 'Unreachable' && <Alert severity="warning">{t.engineUnreachableHelp}</Alert>}

            <Row label={t.extractor}>
              <Chip
                size="small"
                color={data.extractorEnabled ? 'success' : 'default'}
                label={
                  data.extractorEnabled
                    ? extractorMode === 'LocalTesseract'
                      ? `${t.enabled} (Tesseract)`
                      : extractorMode === 'Tika'
                        ? `${t.enabled} (Tika)`
                        : t.enabled
                    : t.disabled
                }
              />
            </Row>
            {extractorHelp && <p className="text-sm text-paper-600">{extractorHelp}</p>}

            {data.engineEnabled && engineMode !== 'Unreachable' && (
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
          {data?.engineEnabled ? (
            <p className="text-sm text-paper-500">{t.reindexHelp}</p>
          ) : (
            <p className="text-sm text-paper-500">{t.engineDisabledHelp}</p>
          )}
          <div className="flex flex-wrap gap-2">
            <Button
              disabled={busy || !data?.engineEnabled || engineMode === 'Unreachable'}
              onClick={() =>
                run(async () => {
                  await api.searchAdmin.reindex();
                  return t.reindexQueued;
                })
              }
            >
              {t.reindex}
            </Button>
            <Button
              variant="outline"
              disabled={busy || failed === 0}
              onClick={() =>
                run(async () => {
                  const { queued } = await api.searchAdmin.retryFailed();
                  return `${queued.toLocaleString('fa-IR')} ${t.retryQueued}`;
                })
              }
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
