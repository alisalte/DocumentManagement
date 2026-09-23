import {
  Alert,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
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
    <Stack spacing={2} sx={{ maxWidth: 900 }}>
      <Stack direction="row" sx={{ alignItems: 'center' }}>
        <Typography variant="h5" component="h1" sx={{ flexGrow: 1 }}>
          {a.workflows}
        </Typography>
        <Button variant="contained" onClick={() => setCreating(true)}>
          {a.newWorkflow}
        </Button>
      </Stack>
      {workflows.isError && <Alert severity="error">{describeError(workflows.error)}</Alert>}
      <Paper variant="outlined">
        <List disablePadding>
          {(workflows.data ?? []).map((workflow) => (
            <ListItemButton key={workflow.id} divider onClick={() => navigate(`/admin/workflows/${workflow.id}`)}>
              <ListItemText primary={workflow.name} secondary={<span dir="ltr">{workflow.code}</span>} />
              <Stack direction="row" spacing={0.5}>
                {!workflow.latestPublishedVersionId && <Chip size="small" color="warning" label={a.unpublished} />}
                {!workflow.isActive && <Chip size="small" label={a.inactive} />}
              </Stack>
            </ListItemButton>
          ))}
        </List>
      </Paper>
      {creating && <CreateDialog onClose={() => setCreating(false)} onCreated={(id) => navigate(`/admin/workflows/${id}`)} />}
    </Stack>
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
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>{a.newWorkflow}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField label={a.name} value={name} onChange={(event) => setName(event.target.value)} required fullWidth />
          <TextField
            label={a.code}
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            required
            fullWidth
            slotProps={{ htmlInput: { dir: 'ltr', maxLength: 64 } }}
          />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{a.cancel}</Button>
        <Button variant="contained" onClick={create} disabled={busy || !code.trim() || !name.trim()}>
          {a.create}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
