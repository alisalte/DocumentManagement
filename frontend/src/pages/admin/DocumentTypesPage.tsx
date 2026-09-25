import { Alert, Button, Card, Chip, Dialog, TextField } from '../../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api } from '../../lib/api';
import { describeError } from '../../strings';
import { a } from './adminStrings';

export function DocumentTypesPage() {
  const navigate = useNavigate();
  const types = useQuery({ queryKey: ['admin-document-types'], queryFn: api.admin.documentTypes });
  const [creating, setCreating] = useState(false);

  return (
    <div className="max-w-4xl space-y-4 sm:space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-bold text-slate-800">{a.documentTypes}</h1>
        <Button onClick={() => setCreating(true)}>{a.newType}</Button>
      </div>

      {types.isError && <Alert severity="error">{describeError(types.error)}</Alert>}

      <Card flush>
        <div className="divide-y divide-slate-100">
          {(types.data ?? []).map((type) => (
            <button
              key={type.id}
              type="button"
              onClick={() => navigate(`/admin/document-types/${type.id}`)}
              className="flex w-full items-center gap-3 px-4 py-3 text-start transition-colors hover:bg-slate-50"
            >
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-slate-800">{type.name}</span>
                <span dir="ltr" className="block truncate text-start text-xs text-slate-500">
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

      {creating && <CreateTypeDialog onClose={() => setCreating(false)} onCreated={(id) => navigate(`/admin/document-types/${id}`)} />}
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
