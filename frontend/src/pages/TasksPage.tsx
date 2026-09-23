import { Alert, Box, Button, Chip, LinearProgress, Paper, Snackbar, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { TaskActionDialog } from '../components/workflow/TaskActionDialog';
import { actionLabels, w } from '../components/workflow/workflowStrings';
import { api, type WorkflowAction, type WorkflowTask } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { describeError } from '../strings';

/** Everything waiting on the signed-in user, overdue first. */
export function TasksPage() {
  const queryClient = useQueryClient();
  const tasks = useQuery({ queryKey: ['tasks'], queryFn: api.workflow.tasks, refetchInterval: 60_000 });
  const [acting, setActing] = useState<{ task: WorkflowTask; action: WorkflowAction } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Typography variant="h5" component="h1">
        {w.inbox}
      </Typography>
      {tasks.isFetching && <LinearProgress />}
      {tasks.isError && <Alert severity="error">{describeError(tasks.error)}</Alert>}
      {tasks.data?.length === 0 && <Typography color="text.secondary">{w.inboxEmpty}</Typography>}

      {tasks.data?.map((task) => (
        <Paper key={task.id} variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { sm: 'center' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography
                component={RouterLink}
                to={`/documents/${task.documentId}`}
                variant="subtitle1"
                sx={{ fontWeight: 600, color: 'inherit', overflowWrap: 'anywhere' }}
              >
                {task.documentTitle}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {w.step}: {task.stepName} · {w.version} <span dir="ltr">{task.versionLabel}</span> · {formatDateTime(task.createdAt)}
              </Typography>
              {task.dueAt && (
                <Typography variant="caption" color={task.isOverdue ? 'error' : 'text.secondary'}>
                  {w.due}: {formatDateTime(task.dueAt)}
                </Typography>
              )}
            </Box>
            {task.isOverdue && <Chip size="small" color="error" label={w.overdue} />}
            <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
              {task.allowedActions.map((action) => (
                <Button
                  key={action}
                  size="small"
                  variant={action === 'Approve' ? 'contained' : 'outlined'}
                  color={action === 'Reject' ? 'error' : 'primary'}
                  onClick={() => setActing({ task, action })}
                >
                  {actionLabels[action]}
                </Button>
              ))}
            </Stack>
          </Stack>
        </Paper>
      ))}

      {acting && (
        <TaskActionDialog
          task={acting.task}
          action={acting.action}
          onClose={() => setActing(null)}
          onDone={async () => {
            setActing(null);
            setNotice(w.sent);
            await queryClient.invalidateQueries({ queryKey: ['tasks'] });
            await queryClient.invalidateQueries({ queryKey: ['workflow', acting.task.documentId] });
          }}
        />
      )}
      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
  );
}
