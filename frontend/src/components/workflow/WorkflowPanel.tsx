import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  LinearProgress,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, type DocumentVersion, type WorkflowAction, type WorkflowInstance, type WorkflowTask } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { useEntityLabel } from '../metadata/EntityPicker';
import { TaskActionDialog } from './TaskActionDialog';
import { actionLabels, statusColors, statusLabels, w } from './workflowStrings';

/**
 * The workflow of one document: every run with its tasks, the buttons for tasks the viewer may
 * act on, and "send for review" for a draft. What is shown follows the server's flags (canAct,
 * canCancel); the server checks again on every click.
 */
export function WorkflowPanel({
  documentId,
  currentVersion,
  canStart,
  onChanged,
}: {
  documentId: string;
  currentVersion: DocumentVersion | null;
  /** The type has a workflow and the viewer may edit the document. */
  canStart: boolean;
  onChanged: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const runs = useQuery({ queryKey: ['workflow', documentId], queryFn: () => api.workflow.forDocument(documentId) });
  const [acting, setActing] = useState<{ task: WorkflowTask; action: WorkflowAction } | null>(null);
  const [cancelling, setCancelling] = useState<WorkflowInstance | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = async (message: string) => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['workflow', documentId] }),
      queryClient.invalidateQueries({ queryKey: ['versions', documentId] }),
      queryClient.invalidateQueries({ queryKey: ['document', documentId] }),
      queryClient.invalidateQueries({ queryKey: ['tasks'] }),
    ]);
    onChanged(message);
  };

  const startable =
    canStart &&
    currentVersion &&
    (currentVersion.approvalStatus === 'Draft' || currentVersion.approvalStatus === 'Cancelled');

  const start = async () => {
    if (!currentVersion) return;
    setBusy(true);
    setError(null);
    try {
      await api.workflow.start(documentId, currentVersion.id);
      await refresh(w.started);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  if (runs.isPending) return <LinearProgress />;
  if (runs.isError) return null; // No VIEW means no panel; the document page already said why.

  return (
    <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
      <Stack spacing={2}>
        <Stack direction="row" sx={{ alignItems: 'center' }}>
          <Typography variant="h6" component="h2" sx={{ flexGrow: 1 }}>
            {w.workflow}
          </Typography>
          {startable && (
            <Button variant="contained" onClick={start} disabled={busy}>
              {w.start}
            </Button>
          )}
        </Stack>

        {error && <Alert severity="error">{error}</Alert>}
        {runs.data.length === 0 && <Typography color="text.secondary">{w.noWorkflow}</Typography>}

        {runs.data.map((run, index) => (
          <Box key={run.id}>
            {index > 0 && <Divider sx={{ mb: 2 }} />}
            <Stack spacing={1}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>
                <Chip size="small" color={statusColors[run.status]} label={statusLabels[run.status]} />
                <Typography variant="body2" dir="ltr">
                  {run.versionLabel}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {formatDateTime(run.startedAt)}
                </Typography>
                <Box sx={{ flexGrow: 1 }} />
                {run.canCancel && (
                  <Button size="small" color="error" onClick={() => setCancelling(run)}>
                    {w.cancel}
                  </Button>
                )}
              </Stack>

              {run.attentionReason && <Alert severity="warning">{w.attention}: {run.attentionReason}</Alert>}
              {run.cancelReason && (
                <Typography variant="body2" color="text.secondary">
                  {run.cancelReason}
                </Typography>
              )}

              {run.tasks.map((task) => (
                <TaskRow key={task.id} task={task} onAct={(action) => setActing({ task, action })} />
              ))}

              {run.skippedSteps.length > 0 && (
                <Typography variant="caption" color="text.secondary">
                  {w.skipped}: {run.skippedSteps.join('، ')}
                </Typography>
              )}
            </Stack>
          </Box>
        ))}
      </Stack>

      {acting && (
        <TaskActionDialog
          task={acting.task}
          action={acting.action}
          onClose={() => setActing(null)}
          onDone={async () => {
            setActing(null);
            await refresh(w.sent);
          }}
        />
      )}

      {cancelling && (
        <CancelDialog
          instance={cancelling}
          onClose={() => setCancelling(null)}
          onDone={async () => {
            setCancelling(null);
            await refresh(w.cancelled);
          }}
        />
      )}
    </Paper>
  );
}

export function TaskRow({ task, onAct }: { task: WorkflowTask; onAct: (action: WorkflowAction) => void }) {
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={1}
      sx={{ alignItems: { sm: 'center' }, py: 0.5, paddingInlineStart: 1, borderInlineStart: 3, borderColor: 'divider' }}
    >
      <Box sx={{ flexGrow: 1, minWidth: 0 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {task.stepName}
        </Typography>
        <Typography variant="caption" color="text.secondary" component="div">
          <Assignee task={task} />
          {task.status === 'Completed' && task.action && <> — {actionLabels[task.action]}</>}
          {task.status === 'Cancelled' && <> — لغوشده</>}
          {task.completedAt && <> · {formatDateTime(task.completedAt)}</>}
          {task.status === 'Pending' && task.dueAt && (
            <>
              {' · '}
              {w.due}: {formatDateTime(task.dueAt)}
            </>
          )}
        </Typography>
        {task.comment && (
          <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
            «{task.comment}»
          </Typography>
        )}
      </Box>
      {task.isOverdue && <Chip size="small" color="error" label={w.overdue} />}
      {task.canAct && (
        <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
          {task.allowedActions.map((action) => (
            <Button
              key={action}
              size="small"
              variant={action === 'Approve' ? 'contained' : 'outlined'}
              color={action === 'Reject' ? 'error' : 'primary'}
              onClick={() => onAct(action)}
            >
              {actionLabels[action]}
            </Button>
          ))}
        </Stack>
      )}
    </Stack>
  );
}

function Assignee({ task }: { task: WorkflowTask }) {
  const user = useEntityLabel('User', task.status === 'Completed' ? (task.completedBy ?? task.assignedUserId) : task.assignedUserId);
  const group = useEntityLabel('Group', task.assignedGroupId);
  if (task.assignedGroupId && task.status !== 'Completed') return <>{w.group}: {group.data ?? '…'}</>;
  if (task.assignedRoleId && task.status !== 'Completed') return <>{w.role}</>;
  return <>{user.data ?? '…'}</>;
}

function CancelDialog({ instance, onClose, onDone }: { instance: WorkflowInstance; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.workflow.cancel(instance.id, reason.trim());
      onDone();
    } catch (caught) {
      setError(describeError(caught));
      setBusy(false);
    }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{w.cancel}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField label={w.cancelReason} value={reason} onChange={(event) => setReason(event.target.value)} required fullWidth />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {w.close}
        </Button>
        <Button color="error" variant="contained" onClick={submit} disabled={busy || !reason.trim()}>
          {w.cancel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
