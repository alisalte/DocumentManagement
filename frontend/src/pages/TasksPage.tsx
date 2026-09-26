import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { TaskActionDialog } from '../components/workflow/TaskActionDialog';
import { actionLabels, w } from '../components/workflow/workflowStrings';
import { Alert, Button, Card, Chip, ProgressBar, Toast } from '../components/ui';
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
    <div className="max-w-[1000px] space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{w.inbox}</h1>
      </div>
      {tasks.isFetching && <ProgressBar />}
      {tasks.isError && <Alert severity="error">{describeError(tasks.error)}</Alert>}
      {tasks.data?.length === 0 && (
        <Card>
          <div className="px-4 py-14 text-center">
            <p className="text-sm font-medium text-paper-600">{w.inboxEmpty}</p>
          </div>
        </Card>
      )}

      <div className="space-y-3">
        {tasks.data?.map((task) => (
          <Card key={task.id}>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <div className="min-w-0 sm:flex-1">
                <RouterLink
                  to={`/documents/${task.documentId}`}
                  className="font-semibold break-words text-ink-800 hover:text-ink-700 hover:underline"
                >
                  {task.documentTitle}
                </RouterLink>
                <p className="mt-1 text-sm text-paper-500">
                  {w.step}: {task.stepName} · {w.version} <span dir="ltr">{task.versionLabel}</span> ·{' '}
                  {formatDateTime(task.createdAt)}
                </p>
                {task.dueAt && (
                  <p className={`mt-0.5 text-xs ${task.isOverdue ? 'text-rose-600' : 'text-paper-500'}`}>
                    {w.due}: {formatDateTime(task.dueAt)}
                  </p>
                )}
              </div>
              {task.isOverdue && <Chip color="error" label={w.overdue} />}
              <div className="flex flex-wrap gap-2">
                {task.allowedActions.map((action) => (
                  <Button
                    key={action}
                    size="sm"
                    variant={action === 'Approve' ? 'primary' : action === 'Reject' ? 'danger' : 'outline'}
                    onClick={() => setActing({ task, action })}
                  >
                    {actionLabels[action]}
                  </Button>
                ))}
              </div>
            </div>
          </Card>
        ))}
      </div>

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
      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} autoHideDuration={4000} />
    </div>
  );
}
