import { Alert, Button, Card, Chip, ProgressBar, Select, Switch, TextField } from '../../components/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { EntityPicker } from '../../components/metadata/EntityPicker';
import { api, type AuditEntry, type AuditFilter, type SealVerification } from '../../lib/api';
import { formatDateTime, parseJalaliDate, toJalaliInput } from '../../lib/dates';
import { useSession } from '../../session';
import { describeError } from '../../strings';
import { actorName, describeUserAgent, headline, readableFields } from './auditDescribe';
import { a, actionLabel } from './auditStrings';

const pageSize = 50;
const verifyDays = 30;

type Range = 'today' | 'week' | 'month' | 'custom';
const rangeDays: Record<Exclude<Range, 'custom'>, number> = { today: 0, week: 6, month: 29 };

/** A Jalali day typed by the user to the instant it starts, in the browser's time zone. */
function dayStart(jalali: string, plusDays = 0): string | null {
  const iso = parseJalaliDate(jalali);
  if (!iso) return null;
  const date = new Date(`${iso}T00:00:00`);
  date.setDate(date.getDate() + plusDays);
  return date.toISOString();
}

/** Midnight, local time, some days ago. */
function midnight(daysAgo: number): Date {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() - daysAgo);
  return date;
}

/** The local calendar day, as the user sees it (not the UTC date, which lags in Iran after midnight). */
function jalaliDaysAgo(days: number): string {
  const date = midnight(days);
  const pad = (value: number) => String(value).padStart(2, '0');
  return toJalaliInput(`${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`);
}

const dayKey = (date: Date) => `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
const dayHeading = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const timeOfDay = new Intl.DateTimeFormat('fa-IR', { hour: '2-digit', minute: '2-digit' });

function headingFor(date: Date): string {
  const key = dayKey(date);
  if (key === dayKey(midnight(0))) return a.todayHeading;
  if (key === dayKey(midnight(1))) return a.yesterdayHeading;
  return dayHeading.format(date);
}

/**
 * The audit viewer (AUDIT_VIEW): who did what, when, in plain sentences grouped by day. Routine
 * machinery is hidden unless asked for; ids are shown by name, and the raw row is one click away
 * under «جزئیات فنی». Export (AUDIT_EXPORT) and the seal check stay available.
 */
export function AuditPage() {
  const { user } = useSession();
  const canExport = !!user && (user.isSystemAdmin || user.systemPermissions.includes('AUDIT_EXPORT'));

  const [range, setRange] = useState<Range>('week');
  const [fromText, setFromText] = useState(jalaliDaysAgo(6));
  const [toText, setToText] = useState(jalaliDaysAgo(0));
  const [action, setAction] = useState('');
  const [outcome, setOutcome] = useState('');
  const [actorType, setActorType] = useState('');
  const [userId, setUserId] = useState<string | null>(null);
  const [document, setDocument] = useState<{ id: string; name: string | null } | null>(null);
  const [showRoutine, setShowRoutine] = useState(false);
  const [moreFilters, setMoreFilters] = useState(false);
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const { filter, dateError } = useMemo(() => {
    let from: string | null;
    let to: string | null;
    if (range === 'custom') {
      from = fromText.trim() ? dayStart(fromText) : null;
      to = toText.trim() ? dayStart(toText, 1) : null;
      if ((fromText.trim() && !from) || (toText.trim() && !to)) {
        return { filter: null, dateError: true };
      }
    } else {
      from = midnight(rangeDays[range]).toISOString();
      to = null;
    }

    const value: AuditFilter = {
      from,
      to,
      action: action || null,
      outcome: outcome || null,
      actorType: actorType || null,
      userId,
      documentId: document?.id ?? null,
      // Asking for one routine action by name shows it, whatever the switch says.
      hideRoutine: !showRoutine && !action,
    };
    return { filter: value, dateError: false };
  }, [range, fromText, toText, action, outcome, actorType, userId, document, showRoutine]);

  const actions = useQuery({ queryKey: ['audit-actions'], queryFn: api.audit.actions, staleTime: Infinity });
  const entries = useInfiniteQuery({
    queryKey: ['audit', filter],
    queryFn: ({ pageParam }) => api.audit.list(filter!, pageParam, pageSize),
    initialPageParam: 0,
    getNextPageParam: (last, all) => (last.length === pageSize ? all.length * pageSize : undefined),
    enabled: !!filter,
  });

  const exportAs = async (format: 'csv' | 'jsonl') => {
    if (!filter) return;
    // An export needs both ends; «until now» is the end of today.
    const bounded = { ...filter, to: filter.to ?? midnight(-1).toISOString() };
    if (!bounded.from) {
      setMessage({ severity: 'error', text: a.exportNeedsRange });
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      await api.audit.export(bounded, format);
      setMessage({ severity: 'success', text: a.exported });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    } finally {
      setBusy(false);
    }
  };

  const rows = entries.data?.pages.flat() ?? [];
  const days = useMemo(() => {
    const groups: { key: string; heading: string; rows: AuditEntry[] }[] = [];
    for (const entry of rows) {
      const date = new Date(entry.occurredAt);
      const key = dayKey(date);
      const last = groups.at(-1);
      if (last?.key === key) last.rows.push(entry);
      else groups.push({ key, heading: headingFor(date), rows: [entry] });
    }
    return groups;
  }, [rows]);

  const showDocument = (entry: AuditEntry) =>
    setDocument({ id: entry.documentId!, name: (entry.documentId && entry.names?.[entry.documentId]) || null });

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{a.title}</h1>
        <p className="mt-1 text-sm text-paper-500">{a.subtitle}</p>
      </div>

      <SealPanel />

      <Card>
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            {(Object.keys(a.ranges) as Range[]).map((key) => (
              <Button key={key} size="sm" variant={range === key ? 'primary' : 'outline'} onClick={() => setRange(key)}>
                {a.ranges[key]}
              </Button>
            ))}
          </div>

          {range === 'custom' && (
            <div className="flex flex-wrap items-end gap-3">
              <TextField
                className="sm:w-44"
                label={a.from}
                value={fromText}
                onChange={(event) => setFromText(event.target.value)}
                placeholder="1404/07/01"
                dir="ltr"
              />
              <TextField
                className="sm:w-44"
                label={a.to}
                value={toText}
                onChange={(event) => setToText(event.target.value)}
                placeholder="1404/07/30"
                dir="ltr"
              />
            </div>
          )}
          {dateError && <Alert severity="error">{a.badDate}</Alert>}

          <div className="flex flex-wrap items-end gap-3">
            <div className="w-full sm:w-64 [&>*]:w-full">
              <EntityPicker kind="User" label={a.user} value={userId} onChange={setUserId} />
            </div>
            <Select className="sm:w-60" label={a.action} value={action} onChange={(event) => setAction(event.target.value)}>
              <option value="">{a.any}</option>
              {[...(actions.data ?? [])]
                .sort((left, right) => actionLabel(left).localeCompare(actionLabel(right), 'fa'))
                .map((code) => (
                  <option key={code} value={code}>
                    {actionLabel(code)}
                  </option>
                ))}
            </Select>
            <Select className="sm:w-44" label={a.outcome} value={outcome} onChange={(event) => setOutcome(event.target.value)}>
              <option value="">{a.any}</option>
              {Object.entries(a.outcomes).map(([code, label]) => (
                <option key={code} value={code}>
                  {label}
                </option>
              ))}
            </Select>
            {moreFilters && (
              <Select className="sm:w-48" label={a.actorType} value={actorType} onChange={(event) => setActorType(event.target.value)}>
                <option value="">{a.any}</option>
                {Object.entries(a.actorTypes).map(([code, label]) => (
                  <option key={code} value={code}>
                    {label}
                  </option>
                ))}
              </Select>
            )}
          </div>

          {document && (
            <Chip
              color="primary"
              label={`${a.onlyDocument} «${document.name ?? document.id}»`}
              onDelete={() => setDocument(null)}
            />
          )}

          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <Switch checked={showRoutine} onChange={(event) => setShowRoutine(event.target.checked)} label={a.showRoutine} />
            <div className="ms-auto flex flex-wrap items-center gap-2">
              <Button size="sm" variant="ghost" onClick={() => setMoreFilters(!moreFilters)}>
                {moreFilters ? a.fewerFilters : a.moreFilters}
              </Button>
              {canExport && (
                <>
                  <Button size="sm" variant="ghost" disabled={busy} onClick={() => exportAs('csv')}>
                    {a.exportCsv}
                  </Button>
                  <Button size="sm" variant="ghost" disabled={busy} onClick={() => exportAs('jsonl')}>
                    JSONL
                  </Button>
                </>
              )}
            </div>
          </div>
        </div>
      </Card>

      {message && <Alert severity={message.severity}>{message.text}</Alert>}
      {entries.isFetching && !entries.isFetchingNextPage && <ProgressBar className="rounded-full" />}
      {entries.isError && <Alert severity="error">{describeError(entries.error)}</Alert>}
      {entries.isSuccess && rows.length === 0 && <p className="py-10 text-center text-sm text-paper-500">{a.empty}</p>}

      {days.map((day) => (
        <section key={day.key} className="space-y-2">
          <h2 className="px-1 text-sm font-semibold text-paper-600">{day.heading}</h2>
          <Card flush className="divide-y divide-paper-100 overflow-hidden">
            {day.rows.map((entry) => (
              <EntryRow key={entry.id} entry={entry} onDocumentHistory={() => showDocument(entry)} />
            ))}
          </Card>
        </section>
      ))}

      {entries.hasNextPage && (
        <Button variant="outline" onClick={() => entries.fetchNextPage()} disabled={entries.isFetchingNextPage}>
          {a.more}
        </Button>
      )}
    </div>
  );
}

const dotColors = { SUCCESS: 'bg-emerald-500', DENIED: 'bg-amber-500', FAILED: 'bg-rose-500' } as const;
const outcomeColors = { SUCCESS: 'success', DENIED: 'warning', FAILED: 'error' } as const;

function EntryRow({ entry, onDocumentHistory }: { entry: AuditEntry; onDocumentHistory: () => void }) {
  const [open, setOpen] = useState(false);
  const { action, object, version } = headline(entry);
  const device = describeUserAgent(entry.userAgent);

  return (
    <div>
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        className="flex w-full items-start gap-3 px-4 py-3 text-start transition-colors hover:bg-paper-100/60 sm:px-5"
      >
        <span className={`mt-1.5 size-2.5 shrink-0 rounded-full ${dotColors[entry.outcome] ?? 'bg-paper-400'}`} aria-hidden />
        <span className="min-w-0 flex-1">
          <span className="block text-sm break-words text-ink-900">
            <span className="font-semibold">{action}</span>
            {object && <> {object}</>}
            {version && (
              <span dir="ltr" className="mx-1.5 text-xs text-paper-500">
                {version}
              </span>
            )}
          </span>
          <span className="mt-0.5 block text-xs text-paper-500">
            {actorName(entry)} · {timeOfDay.format(new Date(entry.occurredAt))}
            {device && <> · {device}</>}
          </span>
        </span>
        {entry.outcome !== 'SUCCESS' && (
          <Chip size="small" color={outcomeColors[entry.outcome] ?? 'default'} label={a.outcomes[entry.outcome] ?? entry.outcome} />
        )}
      </button>

      {open && <EntryDetails entry={entry} onDocumentHistory={onDocumentHistory} />}
    </div>
  );
}

function EntryDetails({ entry, onDocumentHistory }: { entry: AuditEntry; onDocumentHistory: () => void }) {
  const fields = readableFields(entry);

  return (
    <div className="space-y-3 bg-paper-100/40 px-4 pt-1 pb-4 sm:ps-10 sm:pe-5">
      <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1.5 text-sm">
        <dt className="text-paper-500">{a.by}</dt>
        <dd className="text-ink-800">{actorName(entry)}</dd>
        <dt className="text-paper-500">{a.at}</dt>
        <dd className="text-ink-800">{formatDateTime(entry.occurredAt)}</dd>
        {fields.map((field) => (
          <FieldRow key={field.key} field={field} />
        ))}
      </dl>
      {fields.length === 0 && <p className="text-xs text-paper-500">{a.noDetails}</p>}

      {entry.documentId && (
        <div className="flex flex-wrap gap-2">
          <Button as={RouterLink} to={`/documents/${entry.documentId}`} size="sm" variant="outline">
            {a.openDocument}
          </Button>
          <Button size="sm" variant="ghost" onClick={onDocumentHistory}>
            {a.documentHistory}
          </Button>
        </div>
      )}

      <details className="text-xs text-paper-600">
        <summary className="cursor-pointer select-none text-paper-500 hover:text-ink-700">{a.technical}</summary>
        <dl className="mt-2 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
          <Technical label={a.actionCode} value={entry.action} />
          <Technical label={a.ip} value={entry.ipAddress} />
          <Technical label={a.userAgent} value={entry.userAgent} />
          <Technical label={a.document} value={entry.documentId} />
          <Technical label={a.version} value={entry.versionId} />
          <Technical label={a.entity} value={entry.entityId && `${entry.entityType ?? ''} ${entry.entityId}`} />
          <Technical label={a.shareLink} value={entry.shareLinkId} />
          <Technical label={a.correlation} value={entry.correlationId} />
        </dl>
        <pre dir="ltr" className="mt-2 overflow-x-auto rounded-lg bg-paper-100 p-2 font-mono leading-5 whitespace-pre-wrap break-words">
          {pretty(entry.metadata)}
        </pre>
      </details>
    </div>
  );
}

function FieldRow({ field }: { field: ReturnType<typeof readableFields>[number] }) {
  return (
    <>
      <dt className="text-paper-500">{field.label}</dt>
      <dd className="min-w-0 break-words text-ink-800">
        {'change' in field ? (
          <>
            <span className="text-paper-500">{a.changedFrom}</span> «{field.change[0]}»{' '}
            <span className="text-paper-500">{a.changedTo}</span> «{field.change[1]}»
          </>
        ) : (
          field.value
        )}
      </dd>
    </>
  );
}

function Technical({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null;
  return (
    <>
      <dt className="text-paper-500">{label}</dt>
      <dd dir="ltr" className="min-w-0 text-start font-mono break-all">
        {value}
      </dd>
    </>
  );
}

function pretty(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** One sentence on whether the log is intact, the check button, and the seal facts on request. */
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
  const state = !data?.lastVerifiedAt ? 'never' : data.lastVerificationIntact ? 'ok' : 'bad';
  const tone = { ok: 'bg-emerald-500', bad: 'bg-rose-500', never: 'bg-paper-400' }[state];
  const sentence = { ok: a.sealOk, bad: a.sealBad, never: a.sealNever }[state];

  return (
    <Card>
      <div className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <span className={`size-2.5 shrink-0 rounded-full ${tone}`} aria-hidden />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-ink-900">{data ? sentence : a.seals}</p>
            {data?.lastVerifiedAt && (
              <p className="text-xs text-paper-500">
                {a.lastChecked}: {formatDateTime(data.lastVerifiedAt)}
              </p>
            )}
          </div>
          <Button size="sm" variant="outline" onClick={verify} disabled={busy}>
            {busy ? a.verifying : a.verify(verifyDays)}
          </Button>
        </div>

        {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
        {data && !data.keyed && <Alert severity="warning">{a.unkeyedHelp}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        {result && (
          <Alert severity={result.intact ? 'success' : 'error'}>
            {result.intact ? a.intact(result.sealsChecked, result.rowsChecked) : a.broken(result.problems.length)}
            {!result.intact && (
              <ul className="mt-2 list-disc space-y-1 ps-4">
                {result.problems.slice(0, 20).map((problem, index) => (
                  <li key={index}>
                    {a.problemKinds[problem.kind] ?? problem.kind} — {formatDateTime(problem.periodStart)} …{' '}
                    {formatDateTime(problem.periodEnd)}
                  </li>
                ))}
              </ul>
            )}
          </Alert>
        )}

        {data && (
          <details className="text-xs text-paper-600">
            <summary className="cursor-pointer select-none text-paper-500 hover:text-ink-700">{a.sealDetails}</summary>
            <div className="mt-2 flex flex-wrap gap-2">
              <Chip size="small" color={data.keyed ? 'success' : 'warning'} label={data.keyed ? a.keyed : a.unkeyed} />
              <Chip
                size="small"
                variant="outlined"
                label={data.sealedUntil ? `${a.sealedUntil} ${formatDateTime(data.sealedUntil)}` : a.nothingSealed}
              />
              <Chip size="small" variant="outlined" label={`${a.sealCount}: ${data.sealCount.toLocaleString('fa-IR')}`} />
            </div>
          </details>
        )}
      </div>
    </Card>
  );
}
