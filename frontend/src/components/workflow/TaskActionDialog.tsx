import { useState } from 'react';
import { api, ApiError, type WorkflowAction, type WorkflowTask } from '../../lib/api';
import { describeError } from '../../strings';
import { EntityPicker } from '../metadata/EntityPicker';
import { Alert, Button, Dialog, TextArea } from '../ui';
import { actionLabels, w } from './workflowStrings';

const needsComment: WorkflowAction[] = ['Reject', 'RequestChanges'];

/**
 * One dialog for every task action. Asks only for what the action needs: a comment (required for
 * reject and request-changes), a person for forward. RETURN goes to the previous step unless the
 * workflow names another; the server decides and reports if there is nowhere to go.
 */
export function TaskActionDialog({
  task,
  action,
  onClose,
  onDone,
}: {
  task: WorkflowTask;
  action: WorkflowAction;
  onClose: () => void;
  onDone: () => void;
}) {
  const [comment, setComment] = useState('');
  const [forwardTo, setForwardTo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [commentError, setCommentError] = useState<string | null>(null);

  const submit = async () => {
    if (needsComment.includes(action) && !comment.trim()) {
      setCommentError(w.commentRequired);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await api.workflow.act(task.id, action, {
        comment: comment.trim() || null,
        forwardToUserId: action === 'Forward' ? forwardTo : null,
      });
      onDone();
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError && caught.fieldErrors.comment) setCommentError(caught.fieldErrors.comment.join(' '));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      title={`${actionLabels[action]} — ${task.documentTitle}`}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            {w.close}
          </Button>
          <Button
            variant={action === 'Reject' ? 'danger' : 'primary'}
            loading={busy}
            onClick={submit}
            disabled={busy || (action === 'Forward' && !forwardTo)}
          >
            {actionLabels[action]}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-500">
          {w.step}: {task.stepName} · {w.version} <span dir="ltr">{task.versionLabel}</span>
        </p>
        {action === 'Forward' && (
          <EntityPicker kind="User" label={w.forwardTo} value={forwardTo} onChange={setForwardTo} required disabled={busy} />
        )}
        <TextArea
          label={w.comment}
          value={comment}
          onChange={(event) => {
            setComment(event.target.value);
            setCommentError(null);
          }}
          required={needsComment.includes(action)}
          error={!!commentError}
          helperText={commentError ?? undefined}
          rows={3}
          disabled={busy}
        />
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}
