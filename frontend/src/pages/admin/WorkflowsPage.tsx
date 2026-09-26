import { Alert, Button, Card, Chip, Dialog, TextField } from '../../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api } from '../../lib/api';
import { describeError } from '../../strings';
import { a } from './adminStrings';

export function WorkflowsPage() {
  const navigate = useNavigate();
  const workflows = useQuery({ queryKey: ['workflow-definitions'], queryFn: api.workflow.definitions });
  const [creating, setCreating] = useState(false);

  return (
    <div className="max-w-4xl space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{a.workflows}</h1>
        <Button onClick={() => setCreating(true)}>{a.newWorkflow}</Button>
      </div>
      {workflows.isError && <Alert severity="error">{describeError(workflows.error)}</Alert>}
      <Card flush>
        <div className="divide-y divide-paper-100">
          {(workflows.data ?? []).map((workflow) => (
            <button
              key={workflow.id}
              type="button"
              onClick={() => navigate(`/admin/workflows/${workflow.id}`)}
              className="flex w-full items-center gap-3 px-4 py-3 text-start transition-colors hover:bg-paper-50"
            >
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-ink-800">{workflow.name}</span>
                <span dir="ltr" className="block truncate text-start text-xs text-paper-500">
                  {workflow.code}
                </span>
              </span>
              <span className="flex shrink-0 gap-1.5">
                {!workflow.latestPublishedVersionId && <Chip size="small" color="warning" label={a.unpublished} />}
                {!workflow.isActive && <Chip size="small" label={a.inactive} />}
              </span>
            </button>
          ))}
        </div>
      </Card>
      {creating && <CreateDialog onClose={() => setCreating(false)} onCreated={(id) => navigate(`/admin/workflows/${id}`)} />}
    </div>
  );
}

function CreateDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      const { id } = await api.admin.createWorkflow({ code: code.trim(), name: name.trim(), description: null });
      await queryClient.invalidateQueries({ queryKey: ['workflow-definitions'] });
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
      title={a.newWorkflow}
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
          required
          dir="ltr"
          maxLength={64}
        />
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}
