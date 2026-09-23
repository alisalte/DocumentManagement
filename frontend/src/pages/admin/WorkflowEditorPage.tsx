import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  LinearProgress,
  MenuItem,
  Paper,
  Snackbar,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
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
  const roles = useQuery({ queryKey: ['roles'], queryFn: api.admin.roles });

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

  if (admin.isPending) return <LinearProgress />;
  if (admin.isError) return <Alert severity="error">{describeError(admin.error)}</Alert>;

  const { workflow, versions } = admin.data;
  const update = (index: number, patch: Partial<WorkflowStep>) =>
    change(steps.map((step, at) => (at === index ? { ...step, ...patch } : step)));

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Typography variant="h5" component="h1">
        {workflow.name} <Typography component="span" color="text.secondary" dir="ltr">({workflow.code})</Typography>
      </Typography>

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
            <Box sx={{ flexGrow: 1 }}>
              <Typography variant="h6" component="h2">{a.steps}</Typography>
              <Typography variant="body2" color="text.secondary">{a.stepsHelp}</Typography>
            </Box>
            {dirty && <Chip size="small" color="warning" label={a.unsaved} />}
            <Button onClick={save} disabled={busy || !dirty}>{a.saveDraft}</Button>
            <Button variant="contained" onClick={() => setConfirm(true)} disabled={busy}>{a.publish}</Button>
          </Stack>

          {busy && <LinearProgress />}
          {error && (
            <Alert severity="error">
              {error}
              <Box component="ul" sx={{ m: 0, mt: 1, paddingInlineStart: 2 }}>
                {Object.entries(errors).flatMap(([path, messages]) =>
                  messages.map((message) => (
                    <li key={path + message}>
                      <strong dir="ltr">{path}</strong>: {message}
                    </li>
                  )),
                )}
              </Box>
            </Alert>
          )}

          {steps.map((step, index) => (
            <Paper key={index} variant="outlined" sx={{ p: 2 }}>
              <Stack spacing={2}>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                  <TextField label={a.stepName} value={step.name} onChange={(event) => update(index, { name: event.target.value })} required fullWidth />
                  <TextField
                    label={a.code}
                    value={step.code}
                    onChange={(event) => update(index, { code: event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
                    slotProps={{ htmlInput: { dir: 'ltr' } }}
                  />
                  <TextField
                    label={a.sequence}
                    type="number"
                    value={step.sequence}
                    onChange={(event) => update(index, { sequence: Number(event.target.value) || 1 })}
                    helperText={a.sequenceHelp}
                    sx={{ minWidth: 120 }}
                  />
                  <TextField
                    label={a.slaHours}
                    type="number"
                    value={step.slaHours ?? ''}
                    onChange={(event) => update(index, { slaHours: event.target.value ? Number(event.target.value) : null })}
                    sx={{ minWidth: 120 }}
                  />
                </Stack>

                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                  <TextField
                    select
                    label={a.assignee}
                    value={step.assigneeType}
                    onChange={(event) =>
                      update(index, { assigneeType: event.target.value as AssigneeType, assigneeId: null, assigneeFieldCode: null })
                    }
                    sx={{ minWidth: 200 }}
                  >
                    {assigneeTypes.map((type) => (
                      <MenuItem key={type} value={type}>{assigneeTypeLabels[type]}</MenuItem>
                    ))}
                  </TextField>

                  {(step.assigneeType === 'User' || step.assigneeType === 'Group') && (
                    <Box sx={{ flexGrow: 1 }}>
                      <EntityPicker
                        kind={step.assigneeType}
                        label={assigneeTypeLabels[step.assigneeType]}
                        value={step.assigneeId}
                        onChange={(value) => update(index, { assigneeId: value })}
                        required
                      />
                    </Box>
                  )}
                  {step.assigneeType === 'Role' && (
                    <TextField
                      select
                      label={assigneeTypeLabels.Role}
                      value={step.assigneeId ?? ''}
                      onChange={(event) => update(index, { assigneeId: event.target.value || null })}
                      fullWidth
                    >
                      {(roles.data ?? []).map((role) => (
                        <MenuItem key={role.id} value={role.id}>{role.name}</MenuItem>
                      ))}
                    </TextField>
                  )}
                  {step.assigneeType === 'DynamicUserField' && (
                    <TextField
                      label={a.assigneeField}
                      value={step.assigneeFieldCode ?? ''}
                      onChange={(event) => update(index, { assigneeFieldCode: event.target.value || null })}
                      helperText={a.assigneeFieldHelp}
                      fullWidth
                      slotProps={{ htmlInput: { dir: 'ltr' } }}
                    />
                  )}
                  {(step.assigneeType === 'Group' || step.assigneeType === 'Role') && (
                    <TextField
                      select
                      label={a.completionRule}
                      value={step.completionRule}
                      onChange={(event) => update(index, { completionRule: event.target.value as WorkflowStep['completionRule'] })}
                      sx={{ minWidth: 200 }}
                    >
                      <MenuItem value="Any">{a.completionAny}</MenuItem>
                      <MenuItem value="All">{a.completionAll}</MenuItem>
                    </TextField>
                  )}
                </Stack>

                <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 2 }}>
                  <FormControlLabel
                    control={<Switch checked={step.isRequired} onChange={(event) => update(index, { isRequired: event.target.checked })} />}
                    label={a.required}
                  />
                  <FormControlLabel
                    control={<Switch checked={step.allowSelfApproval} onChange={(event) => update(index, { allowSelfApproval: event.target.checked })} />}
                    label={a.allowSelfApproval}
                  />
                </Stack>

                <ActionsEditor
                  actions={step.actions}
                  earlierSteps={steps.filter((other) => other.sequence < step.sequence).map((other) => other.code)}
                  onChange={(actions) => update(index, { actions })}
                />

                <ConditionField value={step.condition} onChange={(condition) => update(index, { condition })} />

                <Box>
                  <Button color="error" size="small" onClick={() => change(steps.filter((_, at) => at !== index))}>
                    {a.remove}
                  </Button>
                </Box>
              </Stack>
            </Paper>
          ))}

          <Box>
            <Button onClick={() => change([...steps, newStep(steps.length, Math.max(0, ...steps.map((step) => step.sequence)) + 1)])}>
              {a.addStep}
            </Button>
          </Box>
        </Stack>
      </Paper>

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="h6" component="h2" sx={{ mb: 1 }}>{a.versions}</Typography>
        {versions.map((version) => (
          <Typography key={version.id} variant="body2">
            <span dir="ltr">v{version.versionNumber}</span> · {version.status === 'Draft' ? a.draft : version.status}
            {version.publishedAt && ` · ${formatDateTime(version.publishedAt)}`} · {version.stepCount} {a.steps}
          </Typography>
        ))}
      </Paper>

      <Dialog open={confirm} onClose={() => setConfirm(false)}>
        <DialogTitle>{a.publish}</DialogTitle>
        <DialogContent>
          <DialogContentText>{a.publishWorkflowConfirm}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(false)}>{a.cancel}</Button>
          <Button variant="contained" onClick={publish}>{a.publish}</Button>
        </DialogActions>
      </Dialog>
      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
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
    <Stack spacing={1}>
      <Typography variant="subtitle2">{a.allowedActions}</Typography>
      <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 1 }}>
        {allActions.map((action) => (
          <FormControlLabel
            key={action}
            control={<Checkbox checked={!!find(action)} onChange={(event) => toggle(action, event.target.checked)} />}
            label={actionLabels[action]}
          />
        ))}
      </Stack>
      {returnAction && earlierSteps.length > 0 && (
        <TextField
          select
          size="small"
          label={a.returnTarget}
          value={returnAction.targetStepCode ?? ''}
          onChange={(event) =>
            onChange(actions.map((candidate) => (candidate.action === 'Return' ? { ...candidate, targetStepCode: event.target.value || null } : candidate)))
          }
          sx={{ maxWidth: 300 }}
        >
          <MenuItem value="">{a.returnPrevious}</MenuItem>
          {earlierSteps.map((code) => (
            <MenuItem key={code} value={code} dir="ltr">{code}</MenuItem>
          ))}
        </TextField>
      )}
    </Stack>
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
    <TextField
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
      multiline
      minRows={1}
      fullWidth
      slotProps={{ htmlInput: { dir: 'ltr', style: { fontFamily: 'monospace' } } }}
    />
  );
}
