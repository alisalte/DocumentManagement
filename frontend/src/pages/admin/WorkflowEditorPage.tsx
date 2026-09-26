import {
  Alert,
  Button,
  Card,
  CenteredSpinner,
  Checkbox,
  Chip,
  Dialog,
  ProgressBar,
  Select,
  Switch,
  TextArea,
  TextField,
  Toast,
} from '../../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useParams } from 'react-router';
import { EntityPicker } from '../../components/metadata/EntityPicker';
import { actionLabels, assigneeTypeLabels } from '../../components/workflow/workflowStrings';
import { api, ApiError, type AssigneeType, type StepAction, type WorkflowAction, type WorkflowStep } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { parseRule } from '../../lib/rules';
import { describeError } from '../../strings';
import { a } from './adminStrings';

const allActions: WorkflowAction[] = ['Approve', 'Reject', 'Return', 'RequestChanges', 'Forward'];
const assigneeTypes = Object.keys(assigneeTypeLabels) as AssigneeType[];

const newStep = (index: number, sequence: number): WorkflowStep => ({
  code: `step_${index + 1}`,
  name: '',
  sequence,
  assigneeType: 'User',
  assigneeId: null,
  assigneeFieldCode: null,
  completionRule: 'Any',
  isRequired: true,
  slaHours: null,
  allowSelfApproval: false,
  condition: undefined,
  actions: [
    { action: 'Approve', commentRequired: false, targetStepCode: null },
    { action: 'Reject', commentRequired: true, targetStepCode: null },
  ],
});

/**
 * Steps of a workflow draft. Steps with the same sequence run in parallel. Publishing freezes the
 * draft; runs already under way keep the version they started with.
 */
export function WorkflowEditorPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const admin = useQuery({ queryKey: ['admin-workflow', id], queryFn: () => api.admin.workflow(id) });
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.admin.roles() });

  const [steps, setSteps] = useState<WorkflowStep[]>([]);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);

  useEffect(() => {
    if (admin.data?.draft && !dirty) setSteps(admin.data.draft);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [admin.data]);

  const change = (next: WorkflowStep[]) => {
    setSteps(next);
    setDirty(true);
  };

  const save = async (): Promise<boolean> => {
    setBusy(true);
    setError(null);
    setErrors({});
    try {
      await api.admin.saveWorkflowDraft(id, steps);
      setDirty(false);
      setNotice(a.saved);
      await queryClient.invalidateQueries({ queryKey: ['admin-workflow', id] });
      return true;
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setErrors(caught.fieldErrors);
      return false;
    } finally {
      setBusy(false);
    }
  };

  const publish = async () => {
    setConfirm(false);
    if (dirty && !(await save())) return;
    setBusy(true);
    try {
      await api.admin.publishWorkflow(id);
      setNotice(a.published);
      await queryClient.invalidateQueries({ queryKey: ['admin-workflow', id] });
      await queryClient.invalidateQueries({ queryKey: ['workflow-definitions'] });
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setErrors(caught.fieldErrors);
    } finally {
      setBusy(false);
    }
  };

  if (admin.isPending) {
    return (
      <Card flush className="max-w-[1100px] overflow-hidden">
        <ProgressBar />
        <CenteredSpinner />
      </Card>
    );
  }
  if (admin.isError) return <Alert severity="error">{describeError(admin.error)}</Alert>;

  const { workflow, versions } = admin.data;
  const update = (index: number, patch: Partial<WorkflowStep>) =>
    change(steps.map((step, at) => (at === index ? { ...step, ...patch } : step)));

  return (
    <div className="max-w-[1100px] space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">
          {workflow.name}{' '}
          <span className="text-sm font-medium text-paper-500" dir="ltr">
            ({workflow.code})
          </span>
        </h1>
        {dirty && <Chip color="warning" label={a.unsaved} />}
      </div>

      <Card flush className="overflow-hidden">
        {busy && <ProgressBar />}
        <div className="space-y-5 p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-ink-800">{a.steps}</h2>
              <p className="mt-1 text-sm text-paper-500">{a.stepsHelp}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant="ghost" onClick={save} disabled={busy || !dirty}>
                {a.saveDraft}
              </Button>
              <Button onClick={() => setConfirm(true)} disabled={busy}>
                {a.publish}
              </Button>
            </div>
          </div>

          {error && (
            <Alert severity="error">
              {error}
              <ul className="mt-1 space-y-0.5 list-disc ps-4">
                {Object.entries(errors).flatMap(([path, messages]) =>
                  messages.map((message) => (
                    <li key={path + message}>
                      <strong dir="ltr">{path}</strong>: {message}
                    </li>
                  )),
                )}
              </ul>
            </Alert>
          )}

          {steps.map((step, index) => (
            <Card key={index}>
              <div className="space-y-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h3 className="min-w-0 truncate text-sm font-semibold text-ink-800">
                    {step.name || <span dir="ltr">{step.code}</span>}
                  </h3>
                  <Button size="sm" variant="danger" onClick={() => change(steps.filter((_, at) => at !== index))}>
                    {a.remove}
                  </Button>
                </div>

                <div className="grid gap-4 sm:grid-cols-2">
                  <TextField label={a.stepName} value={step.name} onChange={(event) => update(index, { name: event.target.value })} required className="sm:col-span-2" />
                  <TextField
                    label={a.code}
                    value={step.code}
                    onChange={(event) => update(index, { code: event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
                    dir="ltr"
                  />
                  <TextField
                    label={a.sequence}
                    type="number"
                    value={step.sequence}
                    onChange={(event) => update(index, { sequence: Number(event.target.value) || 1 })}
                    helperText={a.sequenceHelp}
                  />
                  <TextField
                    label={a.slaHours}
                    type="number"
                    value={step.slaHours ?? ''}
                    onChange={(event) => update(index, { slaHours: event.target.value ? Number(event.target.value) : null })}
                  />
                </div>

                <div className="grid gap-4 sm:grid-cols-2">
                  <Select
                    label={a.assignee}
                    value={step.assigneeType}
                    onChange={(event) =>
                      update(index, { assigneeType: event.target.value as AssigneeType, assigneeId: null, assigneeFieldCode: null })
                    }
                  >
                    {assigneeTypes.map((type) => (
                      <option key={type} value={type}>
                        {assigneeTypeLabels[type]}
                      </option>
                    ))}
                  </Select>

                  {(step.assigneeType === 'User' || step.assigneeType === 'Group') && (
                    <EntityPicker
                      kind={step.assigneeType}
                      label={assigneeTypeLabels[step.assigneeType]}
                      value={step.assigneeId}
                      onChange={(value) => update(index, { assigneeId: value })}
                      required
                    />
                  )}
                  {step.assigneeType === 'Role' && (
                    <Select
                      label={assigneeTypeLabels.Role}
                      value={step.assigneeId ?? ''}
                      onChange={(event) => update(index, { assigneeId: event.target.value || null })}
                    >
                      {(roles.data ?? []).map((role) => (
                        <option key={role.id} value={role.id}>
                          {role.name}
                        </option>
                      ))}
                    </Select>
                  )}
                  {step.assigneeType === 'DynamicUserField' && (
                    <TextField
                      label={a.assigneeField}
                      value={step.assigneeFieldCode ?? ''}
                      onChange={(event) => update(index, { assigneeFieldCode: event.target.value || null })}
                      helperText={a.assigneeFieldHelp}
                      dir="ltr"
                    />
                  )}
                  {(step.assigneeType === 'Group' || step.assigneeType === 'Role') && (
                    <Select
                      label={a.completionRule}
                      value={step.completionRule}
                      onChange={(event) => update(index, { completionRule: event.target.value as WorkflowStep['completionRule'] })}
                    >
                      <option value="Any">{a.completionAny}</option>
                      <option value="All">{a.completionAll}</option>
                    </Select>
                  )}
                </div>

                <div className="flex flex-wrap gap-x-4 gap-y-2">
                  <Switch label={a.required} checked={step.isRequired} onChange={(event) => update(index, { isRequired: event.target.checked })} />
                  <Switch
                    label={a.allowSelfApproval}
                    checked={step.allowSelfApproval}
                    onChange={(event) => update(index, { allowSelfApproval: event.target.checked })}
                  />
                </div>

                <ActionsEditor
                  actions={step.actions}
                  earlierSteps={steps.filter((other) => other.sequence < step.sequence).map((other) => other.code)}
                  onChange={(actions) => update(index, { actions })}
                />

                <ConditionField value={step.condition} onChange={(condition) => update(index, { condition })} />
              </div>
            </Card>
          ))}

          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" onClick={() => change([...steps, newStep(steps.length, Math.max(0, ...steps.map((step) => step.sequence)) + 1)])}>
              {a.addStep}
            </Button>
          </div>
        </div>
      </Card>

      <Card>
        <h2 className="text-base font-semibold text-ink-800">{a.versions}</h2>
        <div className="mt-2 space-y-1">
          {versions.map((version) => (
            <p key={version.id} className="text-sm text-paper-600">
              <span dir="ltr">v{version.versionNumber}</span> · {version.status === 'Draft' ? a.draft : version.status}
              {version.publishedAt && ` · ${formatDateTime(version.publishedAt)}`} · {version.stepCount} {a.steps}
            </p>
          ))}
        </div>
      </Card>

      <Dialog
        open={confirm}
        onClose={() => setConfirm(false)}
        title={a.publish}
        maxWidth="sm"
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirm(false)}>
              {a.cancel}
            </Button>
            <Button onClick={publish}>{a.publish}</Button>
          </>
        }
      >
        <p className="text-sm text-paper-600">{a.publishWorkflowConfirm}</p>
      </Dialog>
      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} />
    </div>
  );
}

function ActionsEditor({
  actions,
  earlierSteps,
  onChange,
}: {
  actions: StepAction[];
  earlierSteps: string[];
  onChange: (actions: StepAction[]) => void;
}) {
  const find = (action: WorkflowAction) => actions.find((candidate) => candidate.action === action);
  const toggle = (action: WorkflowAction, on: boolean) =>
    onChange(
      on
        ? [...actions, { action, commentRequired: action === 'Reject' || action === 'RequestChanges', targetStepCode: null }]
        : actions.filter((candidate) => candidate.action !== action),
    );
  const returnAction = find('Return');

  return (
    <div className="space-y-2">
      <p className="text-sm font-semibold text-ink-800">{a.allowedActions}</p>
      <div className="flex flex-wrap gap-x-4 gap-y-2">
        {allActions.map((action) => (
          <Checkbox
            key={action}
            checked={!!find(action)}
            onChange={(event) => toggle(action, event.target.checked)}
            label={actionLabels[action]}
          />
        ))}
      </div>
      {returnAction && earlierSteps.length > 0 && (
        <Select
          size="sm"
          label={a.returnTarget}
          value={returnAction.targetStepCode ?? ''}
          onChange={(event) =>
            onChange(actions.map((candidate) => (candidate.action === 'Return' ? { ...candidate, targetStepCode: event.target.value || null } : candidate)))
          }
          className="max-w-[300px]"
        >
          <option value="">{a.returnPrevious}</option>
          {earlierSteps.map((code) => (
            <option key={code} value={code} dir="ltr">
              {code}
            </option>
          ))}
        </Select>
      )}
    </div>
  );
}

function ConditionField({ value, onChange }: { value: unknown; onChange: (value: unknown) => void }) {
  const [text, setText] = useState(() => (value === undefined || value === null ? '' : JSON.stringify(value)));
  let problem: string | null = null;
  if (text.trim()) {
    try {
      const parsed = parseRule(JSON.parse(text));
      if ('error' in parsed) problem = parsed.error;
    } catch {
      problem = a.invalidJson;
    }
  }

  return (
    <TextArea
      label={a.stepCondition}
      value={text}
      onChange={(event) => {
        setText(event.target.value);
        if (!event.target.value.trim()) onChange(undefined);
        else {
          try {
            onChange(JSON.parse(event.target.value));
          } catch {
            // Keep the last valid value until the text parses again.
          }
        }
      }}
      error={!!problem}
      helperText={problem ?? a.stepConditionHelp}
      rows={1}
      dir="ltr"
      style={{ fontFamily: 'monospace' }}
    />
  );
}
