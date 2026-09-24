import {
  Alert,
  Box,
  Button,
  Chip,
  Collapse,
  LinearProgress,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
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
    <Stack spacing={2}>
      <Typography variant="h5" component="h1">
        {a.title}
      </Typography>

      <SealPanel />

      <Paper variant="outlined" component="form" onSubmit={apply} sx={{ p: { xs: 2, sm: 3 } }}>
        <Box
          sx={{
            display: 'grid',
            gap: 2,
            gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: 'repeat(4, 1fr)' },
          }}
        >
          <TextField label={a.from} value={fromText} onChange={(event) => setFromText(event.target.value)} placeholder="1403/07/01" slotProps={{ htmlInput: { dir: 'ltr' } }} />
          <TextField label={a.to} value={toText} onChange={(event) => setToText(event.target.value)} placeholder="1403/07/30" slotProps={{ htmlInput: { dir: 'ltr' } }} />
          <TextField select label={a.action} value={action} onChange={(event) => setAction(event.target.value)}>
            <MenuItem value="">{a.any}</MenuItem>
            {(actions.data ?? []).map((code) => (
              <MenuItem key={code} value={code} dir="ltr">
                {code}
              </MenuItem>
            ))}
          </TextField>
          <TextField select label={a.outcome} value={outcome} onChange={(event) => setOutcome(event.target.value)}>
            <MenuItem value="">{a.any}</MenuItem>
            {Object.entries(a.outcomes).map(([code, label]) => (
              <MenuItem key={code} value={code}>
                {label}
              </MenuItem>
            ))}
          </TextField>
          <TextField select label={a.actorType} value={actorType} onChange={(event) => setActorType(event.target.value)}>
            <MenuItem value="">{a.any}</MenuItem>
            {Object.entries(a.actorTypes).map(([code, label]) => (
              <MenuItem key={code} value={code}>
                {label}
              </MenuItem>
            ))}
          </TextField>
          <EntityPicker kind="User" label={a.user} value={userId} onChange={setUserId} />
          <TextField label={a.documentId} value={documentId} onChange={(event) => setDocumentId(event.target.value)} slotProps={{ htmlInput: { dir: 'ltr' } }} />
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Button type="submit" variant="contained">
              {a.apply}
            </Button>
            {canExport && (
              <>
                <Button disabled={busy} onClick={() => exportAs('csv')}>
                  CSV
                </Button>
                <Button disabled={busy} onClick={() => exportAs('jsonl')}>
                  JSONL
                </Button>
              </>
            )}
          </Stack>
        </Box>
        {formError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {formError}
          </Alert>
        )}
      </Paper>

      {message && <Alert severity={message.severity}>{message.text}</Alert>}
      {entries.isPending && <LinearProgress />}
      {entries.isError && <Alert severity="error">{describeError(entries.error)}</Alert>}
      {entries.isSuccess && rows.length === 0 && <Typography color="text.secondary">{a.empty}</Typography>}

      <Stack spacing={1}>
        {rows.map((entry) => (
          <EntryRow key={entry.id} entry={entry} />
        ))}
      </Stack>

      {entries.hasNextPage && (
        <Button onClick={() => entries.fetchNextPage()} disabled={entries.isFetchingNextPage}>
          {a.more}
        </Button>
      )}
    </Stack>
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
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Box
        role="button"
        tabIndex={0}
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        onKeyDown={(event) => (event.key === 'Enter' || event.key === ' ') && setOpen(!open)}
        sx={{
          cursor: 'pointer',
          display: 'grid',
          gap: 1,
          alignItems: 'center',
          gridTemplateColumns: { xs: '1fr auto', md: '11rem 1fr 7rem 12rem 10rem' },
        }}
      >
        <Typography variant="body2" color="text.secondary">
          {formatDateTime(entry.occurredAt)}
        </Typography>
        <Typography variant="body2" dir="ltr" sx={{ fontFamily: 'monospace', textAlign: 'start', gridRow: { xs: 2, md: 'auto' } }}>
          {entry.action}
        </Typography>
        <Box>
          <Chip size="small" color={outcomeColors[entry.outcome] ?? 'default'} label={a.outcomes[entry.outcome] ?? entry.outcome} />
        </Box>
        <Typography variant="body2" noWrap>
          {actor}
        </Typography>
        <Typography variant="body2" dir="ltr" color="text.secondary" noWrap sx={{ display: { xs: 'none', md: 'block' } }}>
          {entry.ipAddress ?? ''}
        </Typography>
      </Box>
      <Collapse in={open} unmountOnExit>
        <Stack spacing={0.5} sx={{ mt: 1.5 }}>
          {entry.documentId && (
            <Typography variant="body2">
              {a.document}:{' '}
              <RouterLink to={`/documents/${entry.documentId}`} dir="ltr">
                {entry.documentId}
              </RouterLink>
            </Typography>
          )}
          <Detail label={a.entity} value={entry.entityType ? `${entry.entityType} ${entry.entityId ?? ''}` : null} />
          <Detail label={a.version} value={entry.versionId} />
          <Detail label={a.shareLink} value={entry.shareLinkId} />
          <Detail label={a.ip} value={entry.ipAddress} />
          <Detail label={a.userAgent} value={entry.userAgent} />
          <Detail label={a.correlation} value={entry.correlationId} />
          <Box
            component="pre"
            dir="ltr"
            sx={{ m: 0, p: 1, bgcolor: 'grey.100', borderRadius: 1, fontSize: 12, overflowX: 'auto', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}
          >
            {pretty(entry.metadata)}
          </Box>
        </Stack>
      </Collapse>
    </Paper>
  );
}

function Detail({ label, value }: { label: string; value: string | null }) {
  if (!value) return null;
  return (
    <Typography variant="body2">
      {label}:{' '}
      <Box component="span" dir="ltr" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
        {value}
      </Box>
    </Typography>
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
    <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
      <Stack spacing={1.5}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>
          <Typography variant="subtitle1" component="h2" sx={{ flexGrow: 1 }}>
            {a.seals}
          </Typography>
          <Button variant="outlined" onClick={verify} disabled={busy}>
            {busy ? a.verifying : a.verify(verifyDays)}
          </Button>
        </Stack>
        {status.isError && <Alert severity="error">{describeError(status.error)}</Alert>}
        {data && (
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
            <Chip size="small" color={data.keyed ? 'success' : 'warning'} label={data.keyed ? a.keyed : a.unkeyed} />
            <Chip size="small" variant="outlined" label={data.sealedUntil ? `${a.sealedUntil} ${formatDateTime(data.sealedUntil)}` : a.nothingSealed} />
            <Chip size="small" variant="outlined" label={`${a.sealCount}: ${data.sealCount.toLocaleString('fa-IR')}`} />
            {data.lastVerifiedAt && (
              <Chip
                size="small"
                color={data.lastVerificationIntact ? 'success' : 'error'}
                label={`${data.lastVerificationIntact ? a.lastIntact : a.lastBroken} (${formatDateTime(data.lastVerifiedAt)})`}
              />
            )}
          </Stack>
        )}
        {data && !data.keyed && <Alert severity="warning">{a.unkeyedHelp}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        {result && (
          <Alert severity={result.intact ? 'success' : 'error'}>
            {result.intact
              ? a.intact(result.sealsChecked, result.rowsChecked)
              : a.broken(result.problems.length)}
            {!result.intact && (
              <Box component="ul" sx={{ m: 0, mt: 1, pl: 2 }}>
                {result.problems.slice(0, 20).map((problem, index) => (
                  <li key={index}>
                    {a.problemKinds[problem.kind] ?? problem.kind} — {formatDateTime(problem.periodStart)} …{' '}
                    {formatDateTime(problem.periodEnd)}{' '}
                    <Box component="span" dir="ltr" sx={{ color: 'text.secondary' }}>
                      ({problem.detail})
                    </Box>
                  </li>
                ))}
              </Box>
            )}
          </Alert>
        )}
      </Stack>
    </Paper>
  );
}
