import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { api, ApiError, type WorkflowAction, type WorkflowTask } from '../../lib/api';
import { describeError } from '../../strings';
import { EntityPicker } from '../metadata/EntityPicker';
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
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {actionLabels[action]} — {task.documentTitle}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {w.step}: {task.stepName} · {w.version} <span dir="ltr">{task.versionLabel}</span>
          </Typography>
          {action === 'Forward' && (
            <EntityPicker kind="User" label={w.forwardTo} value={forwardTo} onChange={setForwardTo} required disabled={busy} />
          )}
          <TextField
            label={w.comment}
            value={comment}
            onChange={(event) => {
              setComment(event.target.value);
              setCommentError(null);
            }}
            required={needsComment.includes(action)}
            error={!!commentError}
            helperText={commentError ?? undefined}
            multiline
            minRows={3}
            fullWidth
            disabled={busy}
          />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {w.close}
        </Button>
        <Button
          variant="contained"
          color={action === 'Reject' ? 'error' : 'primary'}
          onClick={submit}
          disabled={busy || (action === 'Forward' && !forwardTo)}
        >
          {actionLabels[action]}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
