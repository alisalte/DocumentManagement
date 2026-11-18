import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { TaskActionDialog } from '../components/workflow/TaskActionDialog';
import { actionLabels, w } from '../components/workflow/workflowStrings';
import { Alert, Button, Card, Chip, EmptyState, PageHeader, ProgressBar, Toast, cx } from '../components/ui';
import { InboxIcon } from '../components/icons';
import { formatNumber } from '../lib/format';
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
    <div className="mx-auto max-w-[1000px] space-y-5">
      <PageHeader
        title={w.inbox}
        meta={
          !!tasks.data?.length && (
            <span className="rounded-full bg-ink-50 px-2.5 py-0.5 text-xs font-semibold text-ink-700 tabular-nums">
              {formatNumber(tasks.data.length)}
            </span>
          )
        }
        description="سندهایی که برای تأیید، بررسی یا اقدام شما ارسال شده‌اند؛ موارد سررسیدگذشته اول می‌آیند."
      />
      {tasks.isFetching && <ProgressBar />}
      {tasks.isError && <Alert severity="error">{describeError(tasks.error)}</Alert>}
      {tasks.data?.length === 0 && (
        <Card flush>
          <EmptyState icon={<InboxIcon />} title={w.inboxEmpty} description="وقتی سندی برای اقدام شما ارسال شود، اینجا نمایش داده می‌شود." />
        </Card>
      )}

      <div className="space-y-3">
        {tasks.data?.map((task) => (
          <Card key={task.id} className={cx('transition-shadow hover:shadow-[var(--shadow-elevated)]', task.isOverdue && 'border-rose-200 dark:border-rose-500/30')}>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <span className={cx('hidden size-10 shrink-0 place-items-center rounded-xl ring-1 ring-inset sm:grid [&_svg]:size-5', task.isOverdue ? 'bg-rose-50 text-rose-600 ring-rose-100 dark:bg-rose-500/10 dark:ring-rose-500/20' : 'bg-ink-50 text-ink-600 ring-ink-100')}>
                <InboxIcon />
              </span>
              <div className="min-w-0 sm:flex-1">
                <RouterLink
                  to={`/documents/${task.documentId}`}
                  className="font-semibold break-words text-ink-900 hover:text-ink-700"
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
