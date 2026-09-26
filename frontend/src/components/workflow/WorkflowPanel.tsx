import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, type DocumentVersion, type WorkflowAction, type WorkflowInstance, type WorkflowTask } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { useEntityLabel } from '../metadata/EntityPicker';
import { Alert, Button, Card, Chip, Dialog, ProgressBar, TextField, cx, type ChipColor } from '../ui';
import { TaskActionDialog } from './TaskActionDialog';
import { actionLabels, statusColors, statusLabels, w } from './workflowStrings';

/** Maps a run status onto the primitive's chip palette. */
function runChipColor(color: (typeof statusColors)[WorkflowInstance['status']]): ChipColor {
  return color === 'info' ? 'primary' : color;
}

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

  if (runs.isPending) return <Card><ProgressBar /></Card>;
  if (runs.isError) return null; // No VIEW means no panel; the document page already said why.

  return (
    <Card>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="grow text-base font-semibold text-ink-800">{w.workflow}</h2>
          {startable && (
            <Button onClick={start} loading={busy} disabled={busy}>
              {w.start}
            </Button>
          )}
        </div>

        {error && <Alert severity="error">{error}</Alert>}
        {runs.data.length === 0 && <p className="py-8 text-center text-sm text-paper-500">{w.noWorkflow}</p>}

        {runs.data.map((run, index) => (
          <div key={run.id} className={cx('space-y-2', index > 0 && 'border-t border-paper-100 pt-4')}>
            <div className="flex flex-wrap items-center gap-2">
              <Chip size="small" color={runChipColor(statusColors[run.status])} label={statusLabels[run.status]} />
              <span className="text-sm" dir="ltr">
                {run.versionLabel}
              </span>
              <span className="text-xs text-paper-500">{formatDateTime(run.startedAt)}</span>
              <div className="flex-1" />
              {run.canCancel && (
                <Button variant="danger" size="sm" onClick={() => setCancelling(run)}>
                  {w.cancel}
                </Button>
              )}
            </div>

            {run.attentionReason && (
              <Alert severity="warning">
                {w.attention}: {run.attentionReason}
              </Alert>
            )}
            {run.cancelReason && <p className="text-sm text-paper-500">{run.cancelReason}</p>}

            {run.tasks.map((task) => (
              <TaskRow key={task.id} task={task} onAct={(action) => setActing({ task, action })} />
            ))}

            {run.skippedSteps.length > 0 && (
              <p className="text-xs text-paper-500">
                {w.skipped}: {run.skippedSteps.join('، ')}
              </p>
            )}
          </div>
        ))}
      </div>

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
    </Card>
  );
}

export function TaskRow({ task, onAct }: { task: WorkflowTask; onAct: (action: WorkflowAction) => void }) {
  return (
    <div className="flex flex-col gap-2 rounded-lg border-s-[3px] border-paper-200 py-1.5 ps-3 transition-colors hover:bg-paper-50 sm:flex-row sm:items-center sm:gap-3">
      <div className="min-w-0 grow">
        <p className="text-sm font-semibold text-ink-800">{task.stepName}</p>
        <p className="text-xs text-paper-500">
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
        </p>
        {task.comment && <p className="mt-1 text-sm whitespace-pre-wrap text-ink-800">«{task.comment}»</p>}
      </div>
      {task.isOverdue && <Chip size="small" color="error" label={w.overdue} />}
      {task.canAct && (
        <div className="flex flex-wrap gap-2">
          {task.allowedActions.map((action) => (
            <Button
              key={action}
              size="sm"
              variant={action === 'Approve' ? 'primary' : action === 'Reject' ? 'danger' : 'outline'}
              onClick={() => onAct(action)}
            >
              {actionLabels[action]}
            </Button>
          ))}
        </div>
      )}
    </div>
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
    <Dialog
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      title={w.cancel}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            {w.close}
          </Button>
          <Button variant="danger" loading={busy} onClick={submit} disabled={busy || !reason.trim()}>
            {w.cancel}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <TextField label={w.cancelReason} value={reason} onChange={(event) => setReason(event.target.value)} required />
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}
