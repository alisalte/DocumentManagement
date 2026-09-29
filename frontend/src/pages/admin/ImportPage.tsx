import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Dialog,
  ProgressBar,
  Select,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  TextArea,
  TextField,
  Toast,
} from '../../components/ui';
import { api, type ImportJob, type ImportValidationReport } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { useSession } from '../../session';
import { i, itemStatusLabel, jobStatusLabel } from './importStrings';

/** Admin surface for legacy manifest import (phase 10.6). */
export function ImportPage() {
  const { user } = useSession();
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [validation, setValidation] = useState<ImportValidationReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [itemStatus, setItemStatus] = useState('');
  const [form, setForm] = useState({
    name: '',
    sourceSystem: 'legacy',
    manifestJson: '{\n  "schemaVersion": 2,\n  "source": "legacy",\n  "entries": []\n}',
    filesRoot: '',
    failurePolicy: 'ContinueOnError',
    createMissingCategories: false,
    dryRunOnly: false,
  });

  const canManage = !!user && (user.isSystemAdmin || user.systemPermissions.includes('IMPORT_MANAGE'));
  const canRun =
    !!user &&
    (user.isSystemAdmin ||
      user.systemPermissions.includes('IMPORT_RUN') ||
      user.systemPermissions.includes('IMPORT_MANAGE'));
  const canView =
    !!user &&
    (user.isSystemAdmin ||
      user.systemPermissions.includes('IMPORT_VIEW') ||
      user.systemPermissions.includes('IMPORT_RUN') ||
      user.systemPermissions.includes('IMPORT_MANAGE'));

  const jobs = useQuery({
    queryKey: ['imports'],
    queryFn: api.imports.list,
    refetchInterval: 5_000,
    enabled: canView,
  });

  const selected = useMemo(
    () => jobs.data?.find((job) => job.id === selectedId) ?? null,
    [jobs.data, selectedId],
  );

  const items = useQuery({
    queryKey: ['import-items', selectedId, itemStatus],
    queryFn: () => api.imports.items(selectedId!, itemStatus || null, 0, 200),
    enabled: !!selectedId && canView,
    refetchInterval: selected?.status === 'Running' ? 3_000 : false,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['imports'] });
    await queryClient.invalidateQueries({ queryKey: ['import-items'] });
  };

  const run = async (action: () => Promise<void>, success: string) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      setNotice(success);
      await refresh();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const progress = (job: ImportJob) =>
    job.totalItems > 0 ? Math.min(100, Math.round((job.processedItems / job.totalItems) * 100)) : 0;

  if (!canView) {
    return <Alert severity="error">دسترسی واردات ندارید.</Alert>;
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink-900">{i.title}</h1>
          <p className="mt-1 text-sm text-paper-600">{i.subtitle}</p>
        </div>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={() => void refresh()} disabled={busy}>
            {i.refresh}
          </Button>
          {canManage && (
            <Button onClick={() => setCreateOpen(true)} disabled={busy}>
              {i.create}
            </Button>
          )}
        </div>
      </div>

      {error && <Alert severity="error">{error}</Alert>}
      <Toast open={!!notice} message={notice ?? ''} onClose={() => setNotice(null)} />

      <Card flush className="overflow-hidden">
        {jobs.isFetching && <ProgressBar />}
        {!jobs.data?.length && !jobs.isLoading ? (
          <p className="p-6 text-sm text-paper-600">{i.empty}</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH>{i.name}</TH>
                  <TH>{i.sourceSystem}</TH>
                  <TH>{i.status}</TH>
                  <TH>{i.progress}</TH>
                  <TH>{i.succeeded}</TH>
                  <TH>{i.failed}</TH>
                </TR>
              </THead>
              <TBody>
                {(jobs.data ?? []).map((job) => (
                  <TR
                    key={job.id}
                    className={job.id === selectedId ? 'bg-paper-100' : undefined}
                    onClick={() => setSelectedId(job.id)}
                  >
                    <TD className="cursor-pointer font-medium">{job.name}</TD>
                    <TD className="cursor-pointer">{job.sourceSystem}</TD>
                    <TD className="cursor-pointer">{jobStatusLabel(job.status)}</TD>
                    <TD className="cursor-pointer">
                      {progress(job)}% ({job.processedItems}/{job.totalItems})
                    </TD>
                    <TD className="cursor-pointer">{job.succeededItems}</TD>
                    <TD className="cursor-pointer">{job.failedItems}</TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </Card>

      {selected ? (
        <Card className="space-y-4 p-4">
          <div>
            <h2 className="text-lg font-semibold text-ink-900">
              {selected.name} · {jobStatusLabel(selected.status)}
            </h2>
            <p className="mt-1 text-sm text-paper-600">
              {i.processed}: {selected.processedItems} · {i.succeeded}: {selected.succeededItems} · {i.failed}:{' '}
              {selected.failedItems} · {i.skipped}: {selected.skippedItems} · {i.invalid}: {selected.invalidItems} ·{' '}
              {i.bytes}: {selected.bytesProcessed}/{selected.bytesTotal}
            </p>
            {selected.startedAt && (
              <p className="text-xs text-paper-500">
                {formatDateTime(selected.startedAt)}
                {selected.completedAt ? ` → ${formatDateTime(selected.completedAt)}` : ''}
              </p>
            )}
          </div>

          <div className="flex flex-wrap gap-2">
            {canRun && (
              <Button
                disabled={busy}
                onClick={() =>
                  void run(async () => {
                    setValidation(await api.imports.validate(selected.id));
                  }, 'اعتبارسنجی انجام شد')
                }
              >
                {i.validate}
              </Button>
            )}
            {canRun && !selected.dryRunOnly && (
              <Button
                disabled={busy}
                onClick={() => {
                  if (!window.confirm(i.confirmStart)) return;
                  void run(() => api.imports.start(selected.id), 'واردات شروع شد');
                }}
              >
                {i.start}
              </Button>
            )}
            {canRun && selected.status === 'Running' && (
              <Button disabled={busy} onClick={() => void run(() => api.imports.pause(selected.id), 'متوقف شد')}>
                {i.pause}
              </Button>
            )}
            {canRun && (selected.status === 'Paused' || selected.status === 'Failed') && (
              <Button disabled={busy} onClick={() => void run(() => api.imports.resume(selected.id), 'ازسرگیری شد')}>
                {i.resume}
              </Button>
            )}
            {canRun && (
              <Button
                disabled={busy}
                onClick={() => void run(() => api.imports.retryFailed(selected.id), 'خطاها دوباره صف شدند')}
              >
                {i.retry}
              </Button>
            )}
            <Button
              variant="ghost"
              disabled={busy}
              onClick={() =>
                void run(async () => {
                  const report = await api.imports.report(selected.id);
                  const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
                  const url = URL.createObjectURL(blob);
                  const anchor = document.createElement('a');
                  anchor.href = url;
                  anchor.download = `import-${selected.id}.json`;
                  anchor.click();
                  URL.revokeObjectURL(url);
                }, 'گزارش آماده شد')
              }
            >
              {i.downloadReport}
            </Button>
          </div>

          {validation && validation.jobId === selected.id && (
            <Alert severity={validation.invalid ? 'error' : 'success'}>
              {i.validationTitle}: {validation.valid}/{validation.total} معتبر · {validation.invalid} نامعتبر ·{' '}
              {validation.warnings} {i.warnings}
              <ul className="mt-2 list-disc ps-5 text-sm">
                {validation.issues.slice(0, 20).map((issue, index) => (
                  <li key={`${issue.code}-${index}`}>
                    [{issue.severity}] {issue.code}: {issue.message}
                  </li>
                ))}
              </ul>
            </Alert>
          )}

          <Select
            label={i.filterStatus}
            value={itemStatus}
            onChange={(event) => setItemStatus(event.target.value)}
          >
            <option value="">{i.all}</option>
            {['Pending', 'Valid', 'Invalid', 'Ready', 'Running', 'Succeeded', 'Failed', 'Skipped', 'Retryable'].map(
              (status) => (
                <option key={status} value={status}>
                  {itemStatusLabel(status)}
                </option>
              ),
            )}
          </Select>

          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH>{i.sourceId}</TH>
                  <TH>{i.path}</TH>
                  <TH>{i.status}</TH>
                  <TH>{i.error}</TH>
                </TR>
              </THead>
              <TBody>
                {(items.data ?? []).map((item) => (
                  <TR key={item.id}>
                    <TD className="font-mono text-xs">{item.sourceId}</TD>
                    <TD>{item.sourcePath}</TD>
                    <TD>{itemStatusLabel(item.status)}</TD>
                    <TD className="text-sm">
                      {item.errorCode
                        ? `${item.errorCode}: ${item.errorMessage ?? ''}`
                        : (item.targetDocumentId ?? '—')}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        </Card>
      ) : (
        <p className="text-sm text-paper-600">{i.selectJob}</p>
      )}

      <Dialog
        open={createOpen}
        onClose={() => !busy && setCreateOpen(false)}
        title={i.create}
        maxWidth="xl"
        footer={
          <>
            <Button variant="ghost" disabled={busy} onClick={() => setCreateOpen(false)}>
              {i.cancel}
            </Button>
            <Button
              disabled={busy || !form.name.trim()}
              onClick={() =>
                void run(async () => {
                  const created = await api.imports.create({
                    name: form.name.trim(),
                    sourceSystem: form.sourceSystem.trim(),
                    manifestJson: form.manifestJson,
                    filesRoot: form.filesRoot.trim() || null,
                    failurePolicy: form.failurePolicy,
                    createMissingCategories: form.createMissingCategories,
                    dryRunOnly: form.dryRunOnly,
                  });
                  setSelectedId(created.id);
                  setCreateOpen(false);
                }, 'کار واردات ایجاد شد')
              }
            >
              {i.submit}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <TextField
            label={i.name}
            value={form.name}
            onChange={(event) => setForm({ ...form, name: event.target.value })}
          />
          <TextField
            label={i.sourceSystem}
            value={form.sourceSystem}
            onChange={(event) => setForm({ ...form, sourceSystem: event.target.value })}
          />
          <TextField
            label={i.relativeRoot}
            value={form.filesRoot}
            helperText={i.filesRootHint}
            onChange={(event) => setForm({ ...form, filesRoot: event.target.value })}
          />
          <Select
            label={i.failurePolicy}
            value={form.failurePolicy}
            onChange={(event) => setForm({ ...form, failurePolicy: event.target.value })}
          >
            <option value="ContinueOnError">{i.continueOnError}</option>
            <option value="StopOnError">{i.stopOnError}</option>
          </Select>
          <Checkbox
            label={i.createCategories}
            checked={form.createMissingCategories}
            onChange={(event) => setForm({ ...form, createMissingCategories: event.target.checked })}
          />
          <Checkbox
            label={i.dryRunOnly}
            checked={form.dryRunOnly}
            onChange={(event) => setForm({ ...form, dryRunOnly: event.target.checked })}
          />
          <TextArea
            label={i.manifest}
            value={form.manifestJson}
            rows={14}
            onChange={(event) => setForm({ ...form, manifestJson: event.target.value })}
          />
        </div>
      </Dialog>
    </div>
  );
}
