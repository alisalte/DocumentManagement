import { Alert, Button, Card, Chip, Dialog, TextField } from '../../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api } from '../../lib/api';
import { formatNumber } from '../../lib/format';
import { describeError } from '../../strings';
import { a } from './adminStrings';
import { IndicatorPatternField } from './IndicatorPatternField';

export function DocumentTypesPage() {
  const navigate = useNavigate();
  const types = useQuery({ queryKey: ['admin-document-types'], queryFn: api.admin.documentTypes });
  const [creating, setCreating] = useState(false);

  return (
    <div className="max-w-4xl space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{a.documentTypes}</h1>
        <Button onClick={() => setCreating(true)}>{a.newType}</Button>
      </div>

      {types.isError && <Alert severity="error">{describeError(types.error)}</Alert>}

      <Card flush>
        <div className="divide-y divide-paper-100">
          {(types.data ?? []).map((type) => (
            <button
              key={type.id}
              type="button"
              onClick={() => navigate(`/settings/document-types/${type.id}`)}
              className="flex w-full items-center gap-3 px-4 py-3 text-start transition-colors hover:bg-paper-50"
            >
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-ink-800">{type.name}</span>
                <span dir="ltr" className="block truncate text-start text-xs text-paper-500">
                  {type.code}
                </span>
              </span>
              <span className="flex shrink-0 gap-1.5">
                {!type.latestPublishedVersionId && <Chip size="small" color="warning" label={a.unpublished} />}
                {!type.isActive && <Chip size="small" label={a.inactive} />}
              </span>
            </button>
          ))}
        </div>
      </Card>

      <IndicatorSettingsCard />

      {creating && <CreateTypeDialog onClose={() => setCreating(false)} onCreated={(id) => navigate(`/settings/document-types/${id}`)} />}
    </div>
  );
}

function CreateTypeDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      const { id } = await api.admin.createDocumentType({ code: code.trim(), name: name.trim(), description: null });
      await queryClient.invalidateQueries({ queryKey: ['admin-document-types'] });
      onCreated(id);
    } catch (caught) {
      setError(describeError(caught));
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onClose={busy ? () => {} : onClose}
      title={a.newType}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            {a.cancel}
          </Button>
          <Button onClick={create} disabled={busy || !code.trim() || !name.trim()}>
            {a.create}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <TextField label={a.name} value={name} onChange={(event) => setName(event.target.value)} required />
        <TextField
          label={a.code}
          value={code}
          onChange={(event) => setCode(event.target.value.toUpperCase())}
          helperText={a.codeHelp}
          required
          dir="ltr"
          maxLength={64}
        />
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}

/** The default indicator pattern, and numbering the documents that have none yet (BR-10, BR-18). */
function IndicatorSettingsCard() {
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: ['indicator-settings'], queryFn: api.admin.indicatorSettings });
  const backfill = useQuery({ queryKey: ['indicator-backfill'], queryFn: api.admin.indicatorBackfill });
  const [pattern, setPattern] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const value = pattern ?? settings.data?.defaultPattern ?? '';
  const unnumbered = backfill.data?.unnumbered ?? 0;

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true);
    setMessage(null);
    try {
      await action();
      setMessage({ ok: true, text: success });
      await queryClient.invalidateQueries({ queryKey: ['indicator-settings'] });
      await queryClient.invalidateQueries({ queryKey: ['indicator-backfill'] });
    } catch (caught) {
      setMessage({ ok: false, text: describeError(caught) });
    } finally {
      setBusy(false);
      setConfirming(false);
    }
  };

  return (
    <Card>
      <h2 className="text-base font-semibold text-ink-800">{a.indicatorTitle}</h2>
      <p className="mt-1 text-sm text-paper-500">{a.indicatorDefaultHelp}</p>
      <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-start">
        <IndicatorPatternField label={a.indicatorDefaultPattern} value={value} onChange={setPattern} className="flex-1" />
        <Button
          onClick={() => run(() => api.admin.updateIndicatorSettings(value.trim()), a.saved)}
          disabled={busy || !value.trim() || value === settings.data?.defaultPattern}
        >
          {a.save}
        </Button>
      </div>

      {backfill.data && (
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-paper-100 pt-4">
          <p className="text-sm text-ink-800">
            {unnumbered > 0 ? a.indicatorUnnumbered(formatNumber(unnumbered)) : a.indicatorAllNumbered}
          </p>
          {unnumbered > 0 && (
            <Button variant="outline" onClick={() => setConfirming(true)} disabled={busy}>
              {a.indicatorBackfill}
            </Button>
          )}
        </div>
      )}

      {message && <Alert className="mt-4" severity={message.ok ? 'success' : 'error'}>{message.text}</Alert>}

      {confirming && (
        <Dialog
          open
          onClose={busy ? () => {} : () => setConfirming(false)}
          title={a.indicatorBackfill}
          maxWidth="sm"
          footer={
            <>
              <Button variant="ghost" onClick={() => setConfirming(false)} disabled={busy}>
                {a.cancel}
              </Button>
              <Button onClick={() => run(api.admin.startIndicatorBackfill, a.indicatorBackfillStarted)} disabled={busy}>
                {a.indicatorBackfill}
              </Button>
            </>
          }
        >
          <p className="text-sm text-ink-800">{a.indicatorBackfillConfirm}</p>
        </Dialog>
      )}
    </Card>
  );
}
