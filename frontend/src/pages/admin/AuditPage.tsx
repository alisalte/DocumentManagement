import { Alert, Button, Card, Chip, ProgressBar, Select, TextField } from '../../components/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link as RouterLink } from 'react-router';
import { EntityPicker } from '../../components/metadata/EntityPicker';
import { api, type AuditEntry, type AuditFilter, type SealVerification } from '../../lib/api';
import { formatDateTime, parseJalaliDate, toJalaliInput } from '../../lib/dates';
import { useSession } from '../../session';
import { describeError } from '../../strings';
import { a } from './auditStrings';

const pageSize = 50;
const verifyDays = 30;

/** A Jalali day typed by the user to the instant it starts, in the browser's time zone. */
function dayStart(jalali: string, plusDays = 0): string | null {
  const iso = parseJalaliDate(jalali);
  if (!iso) return null;
  const date = new Date(`${iso}T00:00:00`);
  date.setDate(date.getDate() + plusDays);
  return date.toISOString();
}

/** The local calendar day, as the user sees it (not the UTC date, which lags in Iran after midnight). */
function daysAgo(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() - days);
  const pad = (value: number) => String(value).padStart(2, '0');
  return toJalaliInput(`${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`);
}

/**
 * The audit viewer (AUDIT_VIEW): filter the log, read the details of an entry, export a range
 * (AUDIT_EXPORT), and see and re-check the seal chain that makes tampering visible.
 */
export function AuditPage() {
  const { user } = useSession();
  const canExport = !!user && (user.isSystemAdmin || user.systemPermissions.includes('AUDIT_EXPORT'));

  const [fromText, setFromText] = useState(daysAgo(7));
  const [toText, setToText] = useState(daysAgo(0));
  const [action, setAction] = useState('');
  const [outcome, setOutcome] = useState('');
  const [actorType, setActorType] = useState('');
  const [userId, setUserId] = useState<string | null>(null);
  const [documentId, setDocumentId] = useState('');
  const [filter, setFilter] = useState<AuditFilter>(() => ({ from: dayStart(daysAgo(7)), to: dayStart(daysAgo(0), 1) }));
  const [formError, setFormError] = useState<string | null>(null);
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const actions = useQuery({ queryKey: ['audit-actions'], queryFn: api.audit.actions, staleTime: Infinity });
  const entries = useInfiniteQuery({
    queryKey: ['audit', filter],
    queryFn: ({ pageParam }) => api.audit.list(filter, pageParam, pageSize),
    initialPageParam: 0,
    getNextPageParam: (last, all) => (last.length === pageSize ? all.length * pageSize : undefined),
  });

  const apply = (event: FormEvent) => {
    event.preventDefault();
    const from = fromText.trim() ? dayStart(fromText) : null;
    const to = toText.trim() ? dayStart(toText, 1) : null;
    if ((fromText.trim() && !from) || (toText.trim() && !to)) {
      setFormError(a.badDate);
      return;
    }

    setFormError(null);
    setFilter({
      from,
      to,
      action: action || null,
      outcome: outcome || null,
      actorType: actorType || null,
      userId,
      documentId: documentId.trim() || null,
    });
  };

  const run = async (work: () => Promise<string>) => {
    setBusy(true);
    setMessage(null);
    try {
      setMessage({ severity: 'success', text: await work() });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    } finally {
      setBusy(false);
    }
  };

  const exportAs = (format: 'csv' | 'jsonl') => {
    if (!filter.from || !filter.to) {
      setMessage({ severity: 'error', text: a.exportNeedsRange });
      return;
    }

    void run(async () => {
      await api.audit.export(filter, format);
      return a.exported;
    });
  };

  const rows = entries.data?.pages.flat() ?? [];

  return (
    <div className="space-y-4 sm:space-y-5">
      <h1 className="text-xl font-bold text-slate-800">{a.title}</h1>

      <SealPanel />

      <Card>
        <form onSubmit={apply} className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <TextField
              className="sm:w-48"
              label={a.from}
              value={fromText}
              onChange={(event) => setFromText(event.target.value)}
              placeholder="1403/07/01"
              dir="ltr"
            />
            <TextField
              className="sm:w-48"
              label={a.to}
              value={toText}
              onChange={(event) => setToText(event.target.value)}
              placeholder="1403/07/30"
              dir="ltr"
            />
            <Select className="sm:w-56" label={a.action} value={action} onChange={(event) => setAction(event.target.value)}>
              <option value="">{a.any}</option>
              {(actions.data ?? []).map((code) => (
                <option key={code} value={code} dir="ltr">
                  {code}
                </option>
              ))}
            </Select>
            <Select className="sm:w-48" label={a.outcome} value={outcome} onChange={(event) => setOutcome(event.target.value)}>
              <option value="">{a.any}</option>
              {Object.entries(a.outcomes).map(([code, label]) => (
                <option key={code} value={code}>
                  {label}
                </option>
              ))}
            </Select>
            <Select className="sm:w-56" label={a.actorType} value={actorType} onChange={(event) => setActorType(event.target.value)}>
              <option value="">{a.any}</option>
              {Object.entries(a.actorTypes).map(([code, label]) => (
                <option key={code} value={code}>
                  {label}
                </option>
              ))}
            </Select>
            <div className="w-full sm:w-64 [&>*]:w-full">
              <EntityPicker kind="User" label={a.user} value={userId} onChange={setUserId} />
            </div>
            <TextField
              className="sm:w-56"
              label={a.documentId}
              value={documentId}
              onChange={(event) => setDocumentId(event.target.value)}
              dir="ltr"
            />
            <div className="flex flex-wrap items-center gap-2">
              <Button type="submit">{a.apply}</Button>
              {canExport && (
                <>
                  <Button variant="ghost" disabled={busy} onClick={() => exportAs('csv')}>
                    CSV
                  </Button>
                  <Button variant="ghost" disabled={busy} onClick={() => exportAs('jsonl')}>
                    JSONL
                  </Button>
                </>
              )}
            </div>
          </div>
          {formError && <Alert severity="error">{formError}</Alert>}
        </form>
      </Card>

      {message && <Alert severity={message.severity}>{message.text}</Alert>}
      {entries.isPending && <ProgressBar className="rounded-full" />}
      {entries.isError && <Alert severity="error">{describeError(entries.error)}</Alert>}
      {entries.isSuccess && rows.length === 0 && (
        <p className="py-10 text-center text-sm text-slate-500">{a.empty}</p>
      )}

      <div className="space-y-2">
        {rows.map((entry) => (
          <EntryRow key={entry.id} entry={entry} />
        ))}
      </div>

      {entries.hasNextPage && (
        <Button variant="ghost" onClick={() => entries.fetchNextPage()} disabled={entries.isFetchingNextPage}>
          {a.more}
        </Button>
      )}
    </div>
  );
}

const outcomeColors = { SUCCESS: 'success', DENIED: 'warning', FAILED: 'error' } as const;

function EntryRow({ entry }: { entry: AuditEntry }) {
  const [open, setOpen] = useState(false);
  const actor =
    entry.actorType === 'USER'
      ? entry.userName ?? entry.userId ?? '—'
      : entry.actorType === 'SHARELINK'
        ? a.actorTypes.SHARELINK
        : a.actorTypes[entry.actorType] ?? entry.actorType;

  return (
    <Card>
      <div
        role="button"
        tabIndex={0}
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        onKeyDown={(event) => (event.key === 'Enter' || event.key === ' ') && setOpen(!open)}
        className="grid cursor-pointer grid-cols-[1fr_auto] items-center gap-2 md:grid-cols-[11rem_1fr_7rem_12rem_10rem]"
      >
        <span className="min-w-0 text-sm text-slate-500">{formatDateTime(entry.occurredAt)}</span>
        <span dir="ltr" className="col-start-1 row-start-2 min-w-0 font-mono text-sm break-all text-slate-700 md:col-auto md:row-auto">
          {entry.action}
        </span>
        <span className="col-start-2 row-start-1 md:col-auto md:row-auto">
          <Chip
            size="small"
            color={outcomeColors[entry.outcome] ?? 'default'}
            label={a.outcomes[entry.outcome] ?? entry.outcome}
          />
        </span>
        <span className="col-start-2 row-start-2 min-w-0 truncate text-sm text-slate-800 md:col-auto md:row-auto">{actor}</span>
        <span dir="ltr" className="hidden min-w-0 truncate font-mono text-sm text-slate-500 md:block">
          {entry.ipAddress ?? ''}
        </span>
      </div>

      {open && (
        <div className="mt-3 space-y-1.5 border-t border-slate-100 pt-3">
          {entry.documentId && (
            <p className="text-sm text-slate-700">
              {a.document}:{' '}
              <RouterLink to={`/documents/${entry.documentId}`} dir="ltr" className="font-mono text-brand-700 hover:underline">
                {entry.documentId}
              </RouterLink>
            </p>
          )}
          <Detail label={a.entity} value={entry.entityType ? `${entry.entityType} ${entry.entityId ?? ''}` : null} />
          <Detail label={a.version} value={entry.versionId} />
          <Detail label={a.shareLink} value={entry.shareLinkId} />
          <Detail label={a.ip} value={entry.ipAddress} />
          <Detail label={a.userAgent} value={entry.userAgent} />
          <Detail label={a.correlation} value={entry.correlationId} />
          <pre
            dir="ltr"
            className="m-0 overflow-x-auto rounded-lg bg-slate-100 p-2 font-mono text-xs leading-5 whitespace-pre-wrap break-words"
          >
            {pretty(entry.metadata)}
          </pre>
        </div>
      )}
    </Card>
  );
}

function Detail({ label, value }: { label: string; value: string | null }) {
  if (!value) return null;
  return (
    <p className="text-sm text-slate-700">
      {label}:{' '}
      <span dir="ltr" className="font-mono break-all text-slate-600">
        {value}
      </span>
    </p>
  );
}

function pretty(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** How far the log is sealed, with what, and whether the last check found anything. */
function SealPanel() {
  const status = useQuery({ queryKey: ['audit-seals'], queryFn: api.audit.sealStatus });
  const [result, setResult] = useState<SealVerification | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const verify = async () => {
    setBusy(true);
    setError(null);
    try {
      // The recent past, like the daily job; the whole chain is a job for a quiet hour, not a click.
      const from = new Date();
      from.setDate(from.getDate() - verifyDays);
      setResult(await api.audit.verify(from.toISOString(), null));
      await status.refetch();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const data = status.data;
  return (
    <Card>
      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-base font-semibold text-slate-800">{a.seals}</h2>
          <Button variant="outline" onClick={verify} disabled={busy}>
            {busy ? a.verifying : a.verify(verifyDays)}
          </Button>
        </div>
        {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
        {data && (
          <div className="flex flex-wrap gap-2">
            <Chip size="small" color={data.keyed ? 'success' : 'warning'} label={data.keyed ? a.keyed : a.unkeyed} />
            <Chip
              size="small"
              variant="outlined"
              label={data.sealedUntil ? `${a.sealedUntil} ${formatDateTime(data.sealedUntil)}` : a.nothingSealed}
            />
            <Chip size="small" variant="outlined" label={`${a.sealCount}: ${data.sealCount.toLocaleString('fa-IR')}`} />
            {data.lastVerifiedAt && (
              <Chip
                size="small"
                color={data.lastVerificationIntact ? 'success' : 'error'}
                label={`${data.lastVerificationIntact ? a.lastIntact : a.lastBroken} (${formatDateTime(data.lastVerifiedAt)})`}
              />
            )}
          </div>
        )}
        {data && !data.keyed && <Alert severity="warning">{a.unkeyedHelp}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        {result && (
          <Alert severity={result.intact ? 'success' : 'error'}>
            {result.intact
              ? a.intact(result.sealsChecked, result.rowsChecked)
              : a.broken(result.problems.length)}
            {!result.intact && (
              <ul className="mt-2 list-disc space-y-1 ps-4">
                {result.problems.slice(0, 20).map((problem, index) => (
                  <li key={index}>
                    {a.problemKinds[problem.kind] ?? problem.kind} — {formatDateTime(problem.periodStart)} …{' '}
                    {formatDateTime(problem.periodEnd)}{' '}
                    <span dir="ltr" className="text-slate-500">
                      ({problem.detail})
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Alert>
        )}
      </div>
    </Card>
  );
}
