import { Alert, Button, Card, Chip, ProgressBar, Table, TBody, TD, TH, THead, TR, TextField } from '../../components/ui';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router';
import { api, type ExtractionActivity } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { describeError, t } from '../../strings';

const extractionLabels: Record<string, string> = {
  Pending: 'در صف',
  Completed: 'انجام‌شده',
  Failed: 'ناموفق',
  Skipped: 'رد شده',
};

const methodLabels: Record<string, string> = {
  Ocr: 'OCR',
  TextLayer: 'لایهٔ متن',
  None: 'بدون متن',
};

/** Indexing status, a full rebuild and a retry of failed text extractions (ADMIN_MANAGE_SEARCH). */
export function SearchAdminPage() {
  const status = useQuery({ queryKey: ['search-status'], queryFn: api.searchAdmin.status, refetchInterval: 15_000 });
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [method, setMethod] = useState('');
  const [outcome, setOutcome] = useState('');
  const [lookup, setLookup] = useState('');
  const [debounced, setDebounced] = useState('');

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(lookup.trim()), 300);
    return () => clearTimeout(handle);
  }, [lookup]);

  const activity = useQuery({
    queryKey: ['search-extractions', debounced, outcome, method],
    queryFn: () =>
      api.searchAdmin.extractions({
        q: debounced || undefined,
        status: outcome || undefined,
        method: method || undefined,
        take: 40,
      }),
    refetchInterval: 15_000,
  });

  const run = async (action: () => Promise<string>) => {
    setBusy(true);
    setMessage(null);
    try {
      setMessage({ severity: 'success', text: await action() });
      await Promise.all([status.refetch(), activity.refetch()]);
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

  const ocr = data?.ocr;
  const filters: { id: string; label: string; method?: string; outcome?: string }[] = [
    { id: 'all', label: t.ocrAll },
    { id: 'ocr', label: 'OCR', method: 'Ocr' },
    { id: 'text', label: methodLabels.TextLayer, method: 'TextLayer' },
    { id: 'failed', label: extractionLabels.Failed, outcome: 'Failed' },
    { id: 'pending', label: extractionLabels.Pending, outcome: 'Pending' },
  ];

  return (
    <div className="max-w-5xl space-y-5">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.searchAdmin}</h1>
        <p className="mt-1 max-w-3xl text-sm text-paper-600">{t.ocrLead}</p>
      </div>
      {status.isLoading && <ProgressBar className="rounded-full" />}
      {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
      {message && <Alert severity={message.severity}>{message.text}</Alert>}

      {ocr && (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Stat label={t.ocrFiles} value={formatNumber(ocr.ocrFiles)} hint={`${formatNumber(ocr.ocrCharacters)} ${t.ocrCharacters}`} />
          <Stat label={t.textLayerFiles} value={formatNumber(ocr.textLayerFiles)} hint={`${formatNumber(ocr.textCharacters)} نویسه`} />
          <Stat label={t.emptyFiles} value={formatNumber(ocr.emptyFiles)} />
          <Stat label={extractionLabels.Failed} value={formatNumber(failed)} tone={failed > 0 ? 'error' : 'default'} />
        </div>
      )}

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
        <div className="space-y-4">
          <div>
            <h2 className="text-base font-semibold text-ink-900">{t.ocrActivity}</h2>
            <p className="mt-1 text-sm text-paper-500">{t.ocrActivityHelp}</p>
          </div>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <TextField
              label={t.ocrSearchPlaceholder}
              value={lookup}
              onChange={(event) => setLookup(event.target.value)}
              size="sm"
              className="flex-1"
            />
          </div>
          <div className="flex flex-wrap gap-1.5">
            {filters.map((filter) => {
              const active = (filter.method ?? '') === method && (filter.outcome ?? '') === outcome;
              return (
                <button
                  key={filter.id}
                  type="button"
                  onClick={() => {
                    setMethod(filter.method ?? '');
                    setOutcome(filter.outcome ?? '');
                  }}
                  className={
                    active
                      ? 'rounded-full bg-ink-700 px-3 py-1 text-xs font-medium text-white'
                      : 'rounded-full bg-paper-100 px-3 py-1 text-xs font-medium text-ink-800 hover:bg-ink-50'
                  }
                >
                  {filter.label}
                </button>
              );
            })}
          </div>
          {activity.isLoading && <ProgressBar className="rounded-full" />}
          {activity.isError && <Alert severity="error">{describeError(activity.error)}</Alert>}
          {activity.data && activity.data.length === 0 && <p className="text-sm text-paper-500">{t.ocrNothing}</p>}
          {activity.data && activity.data.length > 0 && (
            <Table dense>
              <THead>
                <TR>
                  <TH>فایل</TH>
                  <TH>روش</TH>
                  <TH>وضعیت</TH>
                  <TH>نویسه</TH>
                  <TH>زمان</TH>
                </TR>
              </THead>
              <TBody>
                {activity.data.map((row) => (
                  <ActivityRow key={row.storageObjectId} row={row} />
                ))}
              </TBody>
            </Table>
          )}
        </div>
      </Card>

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

function Stat({
  label,
  value,
  hint,
  tone = 'default',
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: 'default' | 'error';
}) {
  return (
    <Card>
      <p className="text-xs text-paper-500">{label}</p>
      <p className={tone === 'error' ? 'mt-1 text-2xl font-bold text-rose-700' : 'mt-1 text-2xl font-bold text-ink-900'}>{value}</p>
      {hint && <p className="mt-1 text-xs text-paper-500">{hint}</p>}
    </Card>
  );
}

function ActivityRow({ row }: { row: ExtractionActivity }) {
  const name = row.fileName || row.title || 'فایل';
  return (
    <>
      <TR>
        <TD>
          <div className="min-w-0">
            {row.documentId ? (
              <RouterLink to={`/documents/${row.documentId}`} className="font-medium text-ink-800 hover:underline">
                {row.title || name}
              </RouterLink>
            ) : (
              <span className="font-medium text-ink-800">{name}</span>
            )}
            {row.fileName && row.title && <p className="truncate text-xs text-paper-500">{row.fileName}</p>}
          </div>
        </TD>
        <TD>
          <Chip size="small" color={row.method === 'Ocr' ? 'primary' : 'default'} label={methodLabels[row.method] ?? row.method} />
        </TD>
        <TD>
          <Chip
            size="small"
            color={row.status === 'Failed' ? 'error' : row.status === 'Completed' ? 'success' : 'default'}
            label={extractionLabels[row.status] ?? row.status}
          />
        </TD>
        <TD>{formatNumber(row.charCount)}</TD>
        <TD>{row.completedAt ? formatDateTime(row.completedAt) : '—'}</TD>
      </TR>
      {row.error && (
        <TR>
          <TD colSpan={5}>
            <p className="break-words text-xs text-rose-700">{row.error}</p>
          </TD>
        </TR>
      )}
    </>
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
