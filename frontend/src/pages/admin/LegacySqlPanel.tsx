import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert, Button, Card, Checkbox, Dialog, Table, TBody, TD, TH, THead, TR, TextField } from '../../components/ui';
import { api, type LegacyArchiveProbe, type LegacyExtraction, type LegacySqlConnection } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { formatBytes, formatNumber } from '../../lib/format';
import { describeError } from '../../strings';
import { describeLegacyError, legacy, legacyErrors, legacyStatusLabel } from './importStrings';

function describe(caught: unknown): string {
  const code = caught && typeof caught === 'object' && 'code' in caught ? String((caught as { code?: string }).code) : '';
  return legacyErrors[code] ?? describeError(caught);
}

const emptyConnection: LegacySqlConnection = {
  server: '',
  database: 'ArchiveSaman',
  user: '',
  password: '',
  encrypt: true,
  trustServerCertificate: true,
  name: '',
};

function percent(copy: LegacyExtraction): number {
  if (copy.status === 'Completed') return 100;
  if (copy.bytesTotal > 0) return Math.min(100, Math.floor((copy.bytesDone / copy.bytesTotal) * 100));
  return copy.filesTotal > 0 ? Math.min(100, Math.floor((copy.filesDone / copy.filesTotal) * 100)) : 0;
}

/**
 * Copying the old archive straight from its SQL Server: connect, see what is there, copy every
 * file into the import staging folder, then continue with the ordinary import job below.
 */
export function LegacySqlPanel({
  canManage,
  onOpenJob,
  onNotice,
}: {
  canManage: boolean;
  onOpenJob: (jobId: string) => void;
  onNotice: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<LegacySqlConnection>(emptyConnection);
  const [probe, setProbe] = useState<LegacyArchiveProbe | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [resuming, setResuming] = useState<LegacyExtraction | null>(null);

  const copies = useQuery({
    queryKey: ['legacy-sql'],
    queryFn: api.imports.legacySql.list,
    refetchInterval: (query) =>
      query.state.data?.some((copy) => copy.status === 'Queued' || copy.status === 'Running') ? 2_000 : 15_000,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['legacy-sql'] });
    await queryClient.invalidateQueries({ queryKey: ['imports'] });
  };

  const connection = (): LegacySqlConnection => ({
    ...form,
    server: form.server.trim(),
    database: form.database.trim(),
    user: form.user?.trim() || null,
    password: form.password || null,
    name: form.name?.trim() || null,
  });

  const test = async () => {
    setBusy(true);
    setFormError(null);
    setProbe(null);
    try {
      setProbe(await api.imports.legacySql.test(connection()));
    } catch (caught) {
      setFormError(describe(caught));
    } finally {
      setBusy(false);
    }
  };

  const start = async () => {
    if (probe && !window.confirm(legacy.confirmStart(formatNumber(probe.files), formatBytes(probe.bytes)))) return;
    setBusy(true);
    setFormError(null);
    try {
      await api.imports.legacySql.start(connection());
      setOpen(false);
      setProbe(null);
      // The password leaves the browser's memory with the form.
      setForm({ ...emptyConnection, server: form.server, database: form.database, user: form.user });
      onNotice(legacy.started);
      await refresh();
    } catch (caught) {
      setFormError(describe(caught));
    } finally {
      setBusy(false);
    }
  };

  const act = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onNotice(success);
      await refresh();
    } catch (caught) {
      setError(describe(caught));
    } finally {
      setBusy(false);
    }
  };

  const ready = form.server.trim() && form.database.trim();

  return (
    <Card className="space-y-4 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-lg font-semibold text-ink-900">{legacy.title}</h2>
          <p className="mt-1 text-sm text-paper-600">{legacy.subtitle}</p>
        </div>
        {canManage && (
          <Button
            onClick={() => {
              setProbe(null);
              setFormError(null);
              setOpen(true);
            }}
            disabled={busy}
          >
            {legacy.connect}
          </Button>
        )}
      </div>

      {error && <Alert severity="error">{error}</Alert>}

      {!copies.data?.length ? (
        !copies.isLoading && <p className="text-sm text-paper-600">{legacy.empty}</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH>{legacy.copies}</TH>
                <TH>{legacy.status}</TH>
                <TH>{legacy.progress}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {copies.data.map((copy) => {
                const failure = describeLegacyError(copy.lastError);
                const live = (copy.status === 'Queued' || copy.status === 'Running') && !copy.interrupted;
                return (
                  <TR key={copy.id}>
                    <TD className="align-top">
                      <div className="font-medium" dir="auto">
                        {copy.name}
                      </div>
                      <div className="text-xs text-paper-500" dir="ltr">
                        {copy.name === `${copy.database} @ ${copy.server}` ? '' : `${copy.database} @ ${copy.server} `}
                        {copy.sqlUser ? `(${copy.sqlUser})` : ''}
                      </div>
                      <div className="text-xs text-paper-500">{formatDateTime(copy.createdAt)}</div>
                    </TD>
                    <TD className="align-top">
                      {copy.interrupted ? legacy.interrupted : legacyStatusLabel(copy.status)}
                      {failure && (
                        <div className="mt-1 max-w-md text-xs text-red-700" title={copy.lastError ?? undefined}>
                          {failure}
                        </div>
                      )}
                      {copy.status === 'Completed' && (
                        <div className="mt-1 max-w-md text-xs text-paper-600">{legacy.nextStep}</div>
                      )}
                      {copy.warnings && (
                        <details className="mt-1 text-xs text-paper-600">
                          <summary className="cursor-pointer">{legacy.warnings}</summary>
                          <pre className="whitespace-pre-wrap" dir="ltr">
                            {copy.warnings}
                          </pre>
                        </details>
                      )}
                    </TD>
                    <TD className="min-w-48 align-top">
                      <div
                        className="h-2 w-full overflow-hidden rounded bg-paper-200"
                        role="progressbar"
                        aria-valuenow={percent(copy)}
                        aria-valuemin={0}
                        aria-valuemax={100}
                      >
                        <div className="h-full bg-copper-600 transition-all" style={{ width: `${percent(copy)}%` }} />
                      </div>
                      <div className="mt-1 text-xs text-paper-600">
                        {formatNumber(percent(copy))}٪ · {formatNumber(copy.filesDone)} از {formatNumber(copy.filesTotal)}{' '}
                        فایل
                      </div>
                      <div className="text-xs text-paper-600">
                        {formatBytes(copy.bytesDone)} از {formatBytes(copy.bytesTotal)}
                      </div>
                      {(copy.filesReused > 0 || copy.filesEmpty > 0) && (
                        <div className="text-xs text-paper-500">
                          {[
                            copy.filesReused > 0 ? `${legacy.reused}: ${formatNumber(copy.filesReused)}` : null,
                            copy.filesEmpty > 0 ? `${legacy.emptyRows}: ${formatNumber(copy.filesEmpty)}` : null,
                          ]
                            .filter(Boolean)
                            .join(' · ')}
                        </div>
                      )}
                    </TD>
                    <TD className="align-top">
                      <div className="flex flex-wrap gap-2">
                        {copy.importJobId && (
                          <Button size="sm" variant="outline" onClick={() => onOpenJob(copy.importJobId!)}>
                            {legacy.openJob}
                          </Button>
                        )}
                        {canManage && live && !copy.cancelRequested && (
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={busy}
                            onClick={() => {
                              if (!window.confirm(legacy.confirmStop)) return;
                              void act(() => api.imports.legacySql.cancel(copy.id), legacy.stopped);
                            }}
                          >
                            {legacy.stop}
                          </Button>
                        )}
                        {canManage && copy.canResume && (
                          <Button size="sm" disabled={busy} onClick={() => setResuming(copy)}>
                            {legacy.resume}
                          </Button>
                        )}
                      </div>
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        </div>
      )}

      <Dialog
        open={open}
        onClose={() => !busy && setOpen(false)}
        title={legacy.connect}
        maxWidth="lg"
        footer={
          <>
            <Button variant="ghost" disabled={busy} onClick={() => setOpen(false)}>
              انصراف
            </Button>
            <Button variant="outline" disabled={busy || !ready} onClick={() => void test()}>
              {busy && !probe ? legacy.testing : legacy.test}
            </Button>
            <Button disabled={busy || !probe || probe.files === 0} onClick={() => void start()}>
              {legacy.start}
            </Button>
          </>
        }
      >
        <form
          className="space-y-3"
          autoComplete="off"
          onSubmit={(event) => {
            event.preventDefault();
            if (ready) void test();
          }}
        >
          <TextField
            label={legacy.server}
            helperText={legacy.serverHint}
            value={form.server}
            dir="ltr"
            required
            onChange={(event) => {
              setProbe(null);
              setForm({ ...form, server: event.target.value });
            }}
          />
          <TextField
            label={legacy.database}
            value={form.database}
            dir="ltr"
            required
            onChange={(event) => {
              setProbe(null);
              setForm({ ...form, database: event.target.value });
            }}
          />
          <TextField
            label={legacy.user}
            helperText={legacy.userHint}
            value={form.user ?? ''}
            dir="ltr"
            autoComplete="off"
            onChange={(event) => {
              setProbe(null);
              setForm({ ...form, user: event.target.value });
            }}
          />
          <TextField
            label={legacy.password}
            helperText={legacy.passwordHint}
            type="password"
            value={form.password ?? ''}
            dir="ltr"
            autoComplete="new-password"
            onChange={(event) => {
              setProbe(null);
              setForm({ ...form, password: event.target.value });
            }}
          />
          <Checkbox
            label={legacy.encrypt}
            checked={form.encrypt}
            onChange={(event) => {
              setProbe(null);
              setForm({ ...form, encrypt: event.target.checked });
            }}
          />
          <p className="-mt-2 text-xs text-paper-500">{legacy.encryptHint}</p>
          <Checkbox
            label={legacy.trust}
            checked={form.trustServerCertificate}
            onChange={(event) => setForm({ ...form, trustServerCertificate: event.target.checked })}
          />
          <TextField
            label={legacy.name}
            value={form.name ?? ''}
            onChange={(event) => setForm({ ...form, name: event.target.value })}
          />
          <button type="submit" hidden />
        </form>

        {formError && (
          <Alert severity="error" className="mt-3">
            {formError}
          </Alert>
        )}
        {probe && (
          <Alert severity="success" className="mt-3">
            {legacy.probeOk(formatNumber(probe.files), formatBytes(probe.bytes), probe.serverVersion)}
            <ul className="mt-2 list-disc ps-5 text-xs">
              {probe.tables.map((table) => (
                <li key={table.table} dir="ltr" className="text-start">
                  {table.table}:{' '}
                  {table.exists
                    ? `${formatNumber(table.rows)} rows${table.files ? `, ${formatNumber(table.files)} files, ${formatBytes(table.bytes)}` : ''}`
                    : legacy.tableMissing}
                </li>
              ))}
            </ul>
          </Alert>
        )}
      </Dialog>

      {resuming && (
        <ResumeDialog
          copy={resuming}
          onClose={() => setResuming(null)}
          onResumed={async () => {
            setResuming(null);
            onNotice(legacy.resumed);
            await refresh();
          }}
        />
      )}
    </Card>
  );
}

function ResumeDialog({
  copy,
  onClose,
  onResumed,
}: {
  copy: LegacyExtraction;
  onClose: () => void;
  onResumed: () => Promise<void>;
}) {
  const [user, setUser] = useState(copy.sqlUser ?? '');
  const [password, setPassword] = useState('');
  const [encrypt, setEncrypt] = useState(true);
  const [trust, setTrust] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const resume = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.imports.legacySql.resume(copy.id, {
        user: user.trim() || null,
        password: password || null,
        encrypt,
        trustServerCertificate: trust,
      });
      await onResumed();
    } catch (caught) {
      setError(describe(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onClose={() => !busy && onClose()}
      title={legacy.resumeTitle}
      footer={
        <>
          <Button variant="ghost" disabled={busy} onClick={onClose}>
            انصراف
          </Button>
          <Button disabled={busy} onClick={() => void resume()}>
            {legacy.resume}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-paper-600" dir="ltr">
          {copy.database} @ {copy.server}
        </p>
        <p className="text-sm text-paper-600">{legacy.resumeHint}</p>
        <TextField label={legacy.user} value={user} dir="ltr" onChange={(event) => setUser(event.target.value)} />
        <TextField
          label={legacy.password}
          type="password"
          value={password}
          dir="ltr"
          autoComplete="new-password"
          onChange={(event) => setPassword(event.target.value)}
        />
        <Checkbox label={legacy.encrypt} checked={encrypt} onChange={(event) => setEncrypt(event.target.checked)} />
        <Checkbox label={legacy.trust} checked={trust} onChange={(event) => setTrust(event.target.checked)} />
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}
