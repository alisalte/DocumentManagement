import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
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
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router';
import { DynamicForm } from '../../components/metadata/DynamicForm';
import {
  api,
  ApiError,
  type DocumentTypeSchema,
  type DocumentTypeSettings,
  type FieldOption,
  type FieldRuleSchema,
  type FieldSchema,
  type FieldType,
  type Metadata,
  type RuleKind,
} from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { parseRule } from '../../lib/rules';
import { describeError } from '../../strings';
import { a, fieldTypeLabels, ruleKindLabels } from './adminStrings';

const fieldTypes = Object.keys(fieldTypeLabels) as FieldType[];
const choiceTypes: FieldType[] = ['Select', 'MultiSelect'];
const textTypes: FieldType[] = ['Text', 'LongText', 'Url', 'Email', 'Phone'];
const numberTypes: FieldType[] = ['Integer', 'Decimal'];

const newField = (index: number): FieldSchema => ({
  code: `field_${index + 1}`,
  label: { fa: '' },
  type: 'Text',
  isRequired: false,
  isSearchable: false,
  isSortable: false,
  showInList: false,
  isApprovalRelevant: false,
  validation: {},
  options: [],
  displayOrder: index,
  isActive: true,
});

/**
 * Schema editor for one document type. Everything edits the draft; publishing freezes it as the
 * next schema version. The preview runs the same rule evaluator as the real form, so an
 * administrator can try the conditions before anyone files a document against them.
 */
export function DocumentTypeEditorPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const admin = useQuery({ queryKey: ['admin-document-type', id], queryFn: () => api.admin.documentType(id) });

  const [fields, setFields] = useState<FieldSchema[]>([]);
  const [rules, setRules] = useState<FieldRuleSchema[]>([]);
  const [dirty, setDirty] = useState(false);
  const [editing, setEditing] = useState<number | null>(null);
  const [serverErrors, setServerErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmPublish, setConfirmPublish] = useState(false);
  const [preview, setPreview] = useState<Metadata>({});

  useEffect(() => {
    if (admin.data?.draft && !dirty) {
      setFields(admin.data.draft.fields);
      setRules(admin.data.draft.rules);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [admin.data]);

  const previewSchema: DocumentTypeSchema | null = useMemo(
    () =>
      admin.data?.draft
        ? { ...admin.data.draft, fields: fields.map((field, index) => ({ ...field, displayOrder: index })), rules }
        : null,
    [admin.data, fields, rules],
  );

  const change = (nextFields: FieldSchema[], nextRules: FieldRuleSchema[] = rules) => {
    setFields(nextFields);
    setRules(nextRules);
    setDirty(true);
  };

  const saveDraft = async (): Promise<boolean> => {
    setBusy(true);
    setError(null);
    setServerErrors({});
    try {
      await api.admin.saveDraft(id, {
        fields: fields.map((field, index) => ({ ...field, displayOrder: index })),
        rules: rules.map((rule, index) => ({ ...rule, displayOrder: index })),
      });
      setDirty(false);
      setNotice(a.saved);
      await queryClient.invalidateQueries({ queryKey: ['admin-document-type', id] });
      return true;
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setServerErrors(caught.fieldErrors);
      return false;
    } finally {
      setBusy(false);
    }
  };

  const publish = async () => {
    setConfirmPublish(false);
    if (dirty && !(await saveDraft())) return;
    setBusy(true);
    try {
      await api.admin.publish(id);
      setNotice(a.published);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['admin-document-type', id] }),
        queryClient.invalidateQueries({ queryKey: ['document-types'] }),
        queryClient.invalidateQueries({ queryKey: ['schema-latest'] }),
      ]);
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setServerErrors(caught.fieldErrors);
    } finally {
      setBusy(false);
    }
  };

  if (admin.isPending) return <LinearProgress />;
  if (admin.isError) return <Alert severity="error">{describeError(admin.error)}</Alert>;

  const { type, versions } = admin.data;
  const codes = fields.map((field) => field.code);

  return (
    <Stack spacing={2} sx={{ maxWidth: 1100 }}>
      <Typography variant="h5" component="h1">
        {type.name} <Typography component="span" color="text.secondary" dir="ltr">({type.code})</Typography>
      </Typography>

      <SettingsPanel typeId={id} name={type.name} description={type.description} settings={type.settings} isActive={type.isActive} />

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
            <Box sx={{ flexGrow: 1 }}>
              <Typography variant="h6" component="h2">
                {a.draft} {admin.data.draft && <span dir="ltr">(v{admin.data.draft.versionNumber})</span>}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {a.draftHelp}
              </Typography>
            </Box>
            {dirty && <Chip color="warning" size="small" label={a.unsaved} />}
            <Button onClick={saveDraft} disabled={busy || !dirty}>
              {a.saveDraft}
            </Button>
            <Button variant="contained" onClick={() => setConfirmPublish(true)} disabled={busy}>
              {a.publish}
            </Button>
          </Stack>

          {busy && <LinearProgress />}
          {error && (
            <Alert severity="error">
              {error}
              <ErrorList errors={serverErrors} fields={fields} />
            </Alert>
          )}

          <Typography variant="subtitle1" component="h3">
            {a.fields}
          </Typography>
          {fields.length === 0 && <Typography color="text.secondary">{a.noFields}</Typography>}
          <Stack divider={<Divider flexItem />}>
            {fields.map((field, index) => (
              <Stack key={index} direction="row" spacing={1} sx={{ alignItems: 'center', py: 1 }}>
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600 }}>{field.label.fa || '—'}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    <span dir="ltr">{field.code}</span> · {fieldTypeLabels[field.type]}
                    {field.isRequired && ` · ${a.required}`}
                    {field.isApprovalRelevant && ` · ${a.approvalRelevant}`}
                    {!field.isActive && ` · ${a.inactive}`}
                  </Typography>
                </Box>
                <IconButton size="small" disabled={index === 0} onClick={() => change(move(fields, index, -1))} aria-label="up">
                  {a.up}
                </IconButton>
                <IconButton
                  size="small"
                  disabled={index === fields.length - 1}
                  onClick={() => change(move(fields, index, 1))}
                  aria-label="down"
                >
                  {a.down}
                </IconButton>
                <Button size="small" onClick={() => setEditing(index)}>
                  {a.editField}
                </Button>
                <Button size="small" color="error" onClick={() => change(fields.filter((_, at) => at !== index))}>
                  {a.remove}
                </Button>
              </Stack>
            ))}
          </Stack>
          <Box>
            <Button
              onClick={() => {
                change([...fields, newField(fields.length)]);
                setEditing(fields.length);
              }}
            >
              {a.addField}
            </Button>
          </Box>

          <Divider />
          <Typography variant="subtitle1" component="h3">
            {a.rules}
          </Typography>
          {rules.map((rule, index) => (
            <RuleEditor
              key={index}
              rule={rule}
              codes={codes}
              onChange={(next) => change(fields, rules.map((current, at) => (at === index ? next : current)))}
              onRemove={() => change(fields, rules.filter((_, at) => at !== index))}
            />
          ))}
          <Box>
            <Button
              onClick={() =>
                change(fields, [...rules, { kind: 'Show', targets: [], condition: undefined, displayOrder: rules.length }])
              }
            >
              {a.addRule}
            </Button>
          </Box>
        </Stack>
      </Paper>

      {previewSchema && previewSchema.fields.length > 0 && (
        <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
          <Typography variant="h6" component="h2">
            {a.preview}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {a.previewHelp}
          </Typography>
          <DynamicForm schema={previewSchema} value={preview} onChange={setPreview} />
        </Paper>
      )}

      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Typography variant="h6" component="h2" sx={{ mb: 1 }}>
          {a.versions}
        </Typography>
        {versions.map((version) => (
          <Typography key={version.id} variant="body2">
            <span dir="ltr">v{version.versionNumber}</span> · {version.status === 'Draft' ? a.draft : version.status}
            {version.publishedAt && ` · ${formatDateTime(version.publishedAt)}`} · {version.fieldCount} {a.fields}
          </Typography>
        ))}
      </Paper>

      {editing !== null && fields[editing] && (
        <FieldDialog
          field={fields[editing]}
          onClose={() => setEditing(null)}
          onSave={(next) => {
            change(fields.map((current, at) => (at === editing ? next : current)));
            setEditing(null);
          }}
        />
      )}

      <Dialog open={confirmPublish} onClose={() => setConfirmPublish(false)}>
        <DialogTitle>{a.publish}</DialogTitle>
        <DialogContent>
          <DialogContentText>{a.publishConfirm}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmPublish(false)}>{a.cancel}</Button>
          <Button variant="contained" onClick={publish}>
            {a.publish}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
  );
}

function move<T>(items: T[], index: number, by: number): T[] {
  const next = [...items];
  const [item] = next.splice(index, 1);
  next.splice(index + by, 0, item);
  return next;
}

/** Server paths such as "fields[2].code" become "«مبلغ» (code): message". */
function ErrorList({ errors, fields }: { errors: Record<string, string[]>; fields: FieldSchema[] }) {
  const entries = Object.entries(errors);
  if (entries.length === 0) return null;

  const describe = (path: string) => {
    const field = /^fields\[(\d+)\]\.?(.*)$/.exec(path);
    if (field) return `«${fields[Number(field[1])]?.label.fa || fields[Number(field[1])]?.code || field[1]}» ${field[2]}`;
    const rule = /^rules\[(\d+)\]\.?(.*)$/.exec(path);
    if (rule) return `${a.rules} ${Number(rule[1]) + 1} ${rule[2]}`;
    return path;
  };

  return (
    <Box component="ul" sx={{ m: 0, mt: 1, paddingInlineStart: 2 }}>
      {entries.flatMap(([path, messages]) =>
        messages.map((message) => (
          <li key={`${path}-${message}`}>
            <strong>{describe(path)}</strong>: {message}
          </li>
        )),
      )}
    </Box>
  );
}

function SettingsPanel({
  typeId,
  name,
  description,
  settings,
  isActive,
}: {
  typeId: string;
  name: string;
  description: string | null;
  settings: DocumentTypeSettings;
  isActive: boolean;
}) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({ name, description: description ?? '', settings, isActive });
  const workflows = useQuery({ queryKey: ['workflow-definitions'], queryFn: api.workflow.definitions });
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const save = async () => {
    setBusy(true);
    setMessage(null);
    try {
      await api.admin.updateDocumentType(typeId, {
        name: form.name.trim(),
        description: form.description.trim() || null,
        settings: form.settings,
        isActive: form.isActive,
      });
      setMessage({ ok: true, text: a.saved });
      await queryClient.invalidateQueries({ queryKey: ['admin-document-type', typeId] });
    } catch (caught) {
      setMessage({ ok: false, text: describeError(caught) });
    } finally {
      setBusy(false);
    }
  };

  const maxMb = form.settings.maxUploadBytes ? Math.round(form.settings.maxUploadBytes / 1024 / 1024) : '';

  return (
    <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
      <Stack spacing={2}>
        <Typography variant="h6" component="h2">
          {a.settings}
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField label={a.name} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} fullWidth />
          <TextField
            select
            label={a.editPolicy}
            value={form.settings.metadataEditPolicy}
            onChange={(event) =>
              setForm({ ...form, settings: { ...form.settings, metadataEditPolicy: event.target.value as DocumentTypeSettings['metadataEditPolicy'] } })
            }
            fullWidth
          >
            <MenuItem value="NewRevision">{a.policyNewRevision}</MenuItem>
            <MenuItem value="InPlace">{a.policyInPlace}</MenuItem>
          </TextField>
        </Stack>
        <TextField
          label={a.description}
          value={form.description}
          onChange={(event) => setForm({ ...form, description: event.target.value })}
          fullWidth
        />
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField
            select
            label={a.workflowMode}
            value={form.settings.workflowMode ?? 'None'}
            onChange={(event) =>
              setForm({ ...form, settings: { ...form.settings, workflowMode: event.target.value as DocumentTypeSettings['workflowMode'] } })
            }
            fullWidth
          >
            <MenuItem value="None">{a.modeNone}</MenuItem>
            <MenuItem value="Manual">{a.modeManual}</MenuItem>
            <MenuItem value="AutoOnVersion">{a.modeAuto}</MenuItem>
          </TextField>
          <TextField
            select
            label={a.workflow}
            value={form.settings.workflowId ?? ''}
            onChange={(event) => setForm({ ...form, settings: { ...form.settings, workflowId: event.target.value || null } })}
            disabled={(form.settings.workflowMode ?? 'None') === 'None'}
            fullWidth
          >
            <MenuItem value="">—</MenuItem>
            {(workflows.data ?? []).map((workflow) => (
              <MenuItem key={workflow.id} value={workflow.id} disabled={!workflow.latestPublishedVersionId || !workflow.isActive}>
                {workflow.name}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <Autocomplete
            multiple
            freeSolo
            fullWidth
            options={[]}
            value={form.settings.allowedExtensions}
            onChange={(_, next) =>
              setForm({
                ...form,
                settings: { ...form.settings, allowedExtensions: next.map((item) => item.trim().replace(/^\./, '').toLowerCase()).filter(Boolean) },
              })
            }
            renderInput={(params) => <TextField {...params} label={a.allowedExtensions} helperText={a.allowedExtensionsHelp} />}
          />
          <TextField
            label={a.maxUploadMb}
            type="number"
            value={maxMb}
            onChange={(event) =>
              setForm({
                ...form,
                settings: {
                  ...form.settings,
                  maxUploadBytes: event.target.value ? Number(event.target.value) * 1024 * 1024 : null,
                },
              })
            }
            sx={{ minWidth: 200 }}
          />
        </Stack>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <FormControlLabel
            control={<Switch checked={form.isActive} onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />}
            label={a.active}
          />
          <Box sx={{ flexGrow: 1 }} />
          <Button onClick={save} disabled={busy || !form.name.trim()}>
            {a.saveSettings}
          </Button>
        </Stack>
        {message && <Alert severity={message.ok ? 'success' : 'error'}>{message.text}</Alert>}
      </Stack>
    </Paper>
  );
}

function FieldDialog({ field, onClose, onSave }: { field: FieldSchema; onClose: () => void; onSave: (field: FieldSchema) => void }) {
  const [draft, setDraft] = useState<FieldSchema>(field);
  const [defaultText, setDefaultText] = useState(
    draft.defaultValue === undefined || draft.defaultValue === null ? '' : JSON.stringify(draft.defaultValue),
  );
  const defaultError = defaultText.trim() !== '' && tryParse(defaultText) === undefined;
  const set = (patch: Partial<FieldSchema>) => setDraft({ ...draft, ...patch });
  const setValidation = (patch: Partial<FieldSchema['validation']>) => set({ validation: { ...draft.validation, ...patch } });
  const num = (value: string) => (value.trim() === '' ? null : Number(value));

  const save = () => {
    onSave({
      ...draft,
      options: choiceTypes.includes(draft.type) ? draft.options : [],
      defaultValue: defaultText.trim() === '' ? null : tryParse(defaultText),
    });
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{a.editField}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label={a.label}
              value={draft.label.fa}
              onChange={(event) => set({ label: { ...draft.label, fa: event.target.value } })}
              required
              fullWidth
            />
            <TextField
              label={a.labelEn}
              value={draft.label.en ?? ''}
              onChange={(event) => set({ label: { ...draft.label, en: event.target.value || null } })}
              fullWidth
              slotProps={{ htmlInput: { dir: 'ltr' } }}
            />
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label={a.code}
              value={draft.code}
              onChange={(event) => set({ code: event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
              helperText="snake_case"
              required
              fullWidth
              slotProps={{ htmlInput: { dir: 'ltr', maxLength: 63 } }}
            />
            <TextField
              select
              label={a.type}
              value={draft.type}
              onChange={(event) => set({ type: event.target.value as FieldType, validation: {} })}
              fullWidth
            >
              {fieldTypes.map((type) => (
                <MenuItem key={type} value={type}>
                  {fieldTypeLabels[type]}
                </MenuItem>
              ))}
            </TextField>
          </Stack>

          <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 2 }}>
            {(
              [
                ['isRequired', a.required],
                ['isApprovalRelevant', a.approvalRelevant],
                ['isSearchable', a.searchable],
                ['showInList', a.showInList],
                ['isActive', a.fieldActive],
              ] as const
            ).map(([key, label]) => (
              <FormControlLabel
                key={key}
                control={<Switch checked={!!draft[key]} onChange={(event) => set({ [key]: event.target.checked })} />}
                label={label}
              />
            ))}
          </Stack>
          {draft.isApprovalRelevant && (
            <Typography variant="caption" color="text.secondary">
              {a.approvalRelevantHelp}
            </Typography>
          )}

          <TextField
            label={a.helpText}
            value={draft.helpText?.fa ?? ''}
            onChange={(event) => set({ helpText: event.target.value ? { fa: event.target.value } : null })}
            fullWidth
          />

          {textTypes.includes(draft.type) && (
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField label={a.minLength} type="number" value={draft.validation.minLength ?? ''} onChange={(event) => setValidation({ minLength: num(event.target.value) })} />
              <TextField label={a.maxLength} type="number" value={draft.validation.maxLength ?? ''} onChange={(event) => setValidation({ maxLength: num(event.target.value) })} />
              <TextField
                label={a.pattern}
                value={draft.validation.pattern ?? ''}
                onChange={(event) => setValidation({ pattern: event.target.value || null })}
                fullWidth
                slotProps={{ htmlInput: { dir: 'ltr' } }}
              />
              <TextField
                label={a.patternMessage}
                value={draft.validation.patternMessage?.fa ?? ''}
                onChange={(event) => setValidation({ patternMessage: event.target.value ? { fa: event.target.value } : null })}
                fullWidth
              />
            </Stack>
          )}

          {numberTypes.includes(draft.type) && (
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField label={a.min} type="number" value={draft.validation.min ?? ''} onChange={(event) => setValidation({ min: num(event.target.value) })} />
              <TextField label={a.max} type="number" value={draft.validation.max ?? ''} onChange={(event) => setValidation({ max: num(event.target.value) })} />
              {draft.type === 'Decimal' && (
                <TextField label={a.scale} type="number" value={draft.validation.scale ?? ''} onChange={(event) => setValidation({ scale: num(event.target.value) })} />
              )}
            </Stack>
          )}

          {draft.type === 'Date' && (
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField label={a.minDate} value={draft.validation.minDate ?? ''} onChange={(event) => setValidation({ minDate: event.target.value || null })} slotProps={{ htmlInput: { dir: 'ltr' } }} />
              <TextField label={a.maxDate} value={draft.validation.maxDate ?? ''} onChange={(event) => setValidation({ maxDate: event.target.value || null })} slotProps={{ htmlInput: { dir: 'ltr' } }} />
            </Stack>
          )}

          {choiceTypes.includes(draft.type) && (
            <OptionsEditor options={draft.options} onChange={(options) => set({ options })} />
          )}
          {draft.type === 'MultiSelect' && (
            <TextField label={a.maxItems} type="number" value={draft.validation.maxItems ?? ''} onChange={(event) => setValidation({ maxItems: num(event.target.value) })} />
          )}

          <TextField
            label={a.defaultValue}
            value={defaultText}
            onChange={(event) => setDefaultText(event.target.value)}
            error={defaultError}
            helperText={defaultError ? a.invalidJson : 'مثال: "north" یا 10 یا true'}
            fullWidth
            slotProps={{ htmlInput: { dir: 'ltr' } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{a.cancel}</Button>
        <Button variant="contained" onClick={save} disabled={!draft.label.fa.trim() || !draft.code || defaultError}>
          {a.save}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function OptionsEditor({ options, onChange }: { options: FieldOption[]; onChange: (options: FieldOption[]) => void }) {
  const update = (index: number, patch: Partial<FieldOption>) =>
    onChange(options.map((option, at) => (at === index ? { ...option, ...patch } : option)));

  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2">{a.options}</Typography>
      {options.map((option, index) => (
        <Stack key={index} direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
          <TextField size="small" label={a.optionValue} value={option.value} onChange={(event) => update(index, { value: event.target.value })} slotProps={{ htmlInput: { dir: 'ltr' } }} />
          <TextField size="small" label={a.optionLabel} value={option.label.fa} onChange={(event) => update(index, { label: { ...option.label, fa: event.target.value } })} fullWidth />
          <FormControlLabel control={<Switch size="small" checked={option.isActive} onChange={(event) => update(index, { isActive: event.target.checked })} />} label={a.fieldActive} />
          <Button size="small" color="error" onClick={() => onChange(options.filter((_, at) => at !== index))}>
            {a.remove}
          </Button>
        </Stack>
      ))}
      <Box>
        <Button size="small" onClick={() => onChange([...options, { value: '', label: { fa: '' }, displayOrder: options.length, isActive: true }])}>
          {a.addOption}
        </Button>
      </Box>
    </Stack>
  );
}

function RuleEditor({
  rule,
  codes,
  onChange,
  onRemove,
}: {
  rule: FieldRuleSchema;
  codes: string[];
  onChange: (rule: FieldRuleSchema) => void;
  onRemove: () => void;
}) {
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField
            select
            label={a.ruleKind}
            value={rule.kind}
            onChange={(event) => {
              const kind = event.target.value as RuleKind;
              onChange({ ...rule, kind, assertion: kind === 'Validate' ? rule.assertion : undefined });
            }}
            sx={{ minWidth: 180 }}
          >
            {(Object.keys(ruleKindLabels) as RuleKind[]).map((kind) => (
              <MenuItem key={kind} value={kind}>
                {ruleKindLabels[kind]}
              </MenuItem>
            ))}
          </TextField>
          <Autocomplete
            multiple
            fullWidth
            options={codes}
            value={rule.targets}
            onChange={(_, targets) => onChange({ ...rule, targets })}
            renderInput={(params) => <TextField {...params} label={a.targets} />}
          />
          <Button color="error" onClick={onRemove}>
            {a.remove}
          </Button>
        </Stack>
        <ExpressionField label={a.condition} value={rule.condition} codes={codes} onChange={(condition) => onChange({ ...rule, condition })} />
        {rule.kind === 'Validate' && (
          <>
            <ExpressionField label={a.assertion} value={rule.assertion} codes={codes} onChange={(assertion) => onChange({ ...rule, assertion })} />
            <TextField
              label={a.message}
              value={rule.message?.fa ?? ''}
              onChange={(event) => onChange({ ...rule, message: event.target.value ? { fa: event.target.value } : null })}
              fullWidth
            />
          </>
        )}
      </Stack>
    </Paper>
  );
}

/** A rule expression as JSON text, checked as you type with the same parser the form uses. */
function ExpressionField({
  label,
  value,
  codes,
  onChange,
}: {
  label: string;
  value: unknown;
  codes: string[];
  onChange: (value: unknown) => void;
}) {
  const [text, setText] = useState(() => (value === undefined || value === null ? '' : JSON.stringify(value)));

  const problem = useMemo(() => {
    if (text.trim() === '') return null;
    const json = tryParse(text);
    if (json === undefined) return a.invalidJson;
    const parsed = parseRule(json);
    if ('error' in parsed) return parsed.error;
    const unknown = referencedFields(json).filter((field) => !codes.includes(field));
    return unknown.length > 0 ? `${a.unknownField}: ${unknown.join(', ')}` : null;
  }, [text, codes]);

  return (
    <TextField
      label={label}
      value={text}
      onChange={(event) => {
        setText(event.target.value);
        const json = tryParse(event.target.value);
        if (event.target.value.trim() === '') onChange(undefined);
        else if (json !== undefined) onChange(json);
      }}
      error={!!problem}
      helperText={problem ?? a.conditionHelp}
      multiline
      minRows={2}
      fullWidth
      slotProps={{ htmlInput: { dir: 'ltr', style: { fontFamily: 'monospace' } } }}
    />
  );
}

function referencedFields(json: unknown): string[] {
  if (!json || typeof json !== 'object') return [];
  if (Array.isArray(json)) return json.flatMap(referencedFields);
  const record = json as Record<string, unknown>;
  const own = typeof record.field === 'string' ? [record.field] : [];
  return [...own, ...Object.values(record).flatMap(referencedFields)];
}

function tryParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
