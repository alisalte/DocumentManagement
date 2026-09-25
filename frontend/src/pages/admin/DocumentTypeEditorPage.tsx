import {
  Alert,
  Button,
  Card,
  CenteredSpinner,
  Checkbox,
  Chip,
  ChipsInput,
  Dialog,
  IconButton,
  ProgressBar,
  Select,
  Switch,
  TextArea,
  TextField,
  Toast,
} from '../../components/ui';
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

  if (admin.isPending) {
    return (
      <Card flush className="max-w-[1100px] overflow-hidden">
        <ProgressBar />
        <CenteredSpinner />
      </Card>
    );
  }
  if (admin.isError) return <Alert severity="error">{describeError(admin.error)}</Alert>;

  const { type, versions } = admin.data;
  const codes = fields.map((field) => field.code);

  return (
    <div className="max-w-[1100px] space-y-4 sm:space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-bold text-slate-800">
          {type.name}{' '}
          <span className="text-sm font-medium text-slate-500" dir="ltr">
            ({type.code})
          </span>
        </h1>
        {dirty && <Chip color="warning" label={a.unsaved} />}
      </div>

      <SettingsPanel typeId={id} name={type.name} description={type.description} settings={type.settings} isActive={type.isActive} />

      <Card flush className="overflow-hidden">
        {busy && <ProgressBar />}
        <div className="space-y-5 p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-slate-800">
                {a.draft}{' '}
                {admin.data.draft && (
                  <span className="text-sm font-medium text-slate-500" dir="ltr">
                    (v{admin.data.draft.versionNumber})
                  </span>
                )}
              </h2>
              <p className="mt-1 text-sm text-slate-500">{a.draftHelp}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant="ghost" onClick={saveDraft} disabled={busy || !dirty}>
                {a.saveDraft}
              </Button>
              <Button onClick={() => setConfirmPublish(true)} disabled={busy}>
                {a.publish}
              </Button>
            </div>
          </div>

          {error && (
            <Alert severity="error">
              {error}
              <ErrorList errors={serverErrors} fields={fields} />
            </Alert>
          )}

          <section className="space-y-3">
            <div className="flex items-center justify-between gap-2">
              <h3 className="text-sm font-semibold text-slate-800">{a.fields}</h3>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  change([...fields, newField(fields.length)]);
                  setEditing(fields.length);
                }}
              >
                {a.addField}
              </Button>
            </div>
            {fields.length === 0 && <p className="py-8 text-center text-sm text-slate-500">{a.noFields}</p>}
            {fields.length > 0 && (
              <ul className="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200 bg-white">
                {fields.map((field, index) => (
                  <li key={index} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2.5 sm:px-4">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-semibold text-slate-800">{field.label.fa || '—'}</p>
                      <p className="mt-0.5 truncate text-xs text-slate-500">
                        <span dir="ltr">{field.code}</span> · {fieldTypeLabels[field.type]}
                        {field.isRequired && ` · ${a.required}`}
                        {field.isApprovalRelevant && ` · ${a.approvalRelevant}`}
                        {!field.isActive && ` · ${a.inactive}`}
                      </p>
                    </div>
                    <div className="flex shrink-0 items-center gap-1">
                      <IconButton label="up" size="sm" disabled={index === 0} onClick={() => change(move(fields, index, -1))}>
                        {a.up}
                      </IconButton>
                      <IconButton
                        label="down"
                        size="sm"
                        disabled={index === fields.length - 1}
                        onClick={() => change(move(fields, index, 1))}
                      >
                        {a.down}
                      </IconButton>
                      <Button size="sm" variant="ghost" onClick={() => setEditing(index)}>
                        {a.editField}
                      </Button>
                      <Button size="sm" variant="danger" onClick={() => change(fields.filter((_, at) => at !== index))}>
                        {a.remove}
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="space-y-3 border-t border-slate-100 pt-5">
            <div className="flex items-center justify-between gap-2">
              <h3 className="text-sm font-semibold text-slate-800">{a.rules}</h3>
              <Button
                size="sm"
                variant="ghost"
                onClick={() =>
                  change(fields, [...rules, { kind: 'Show', targets: [], condition: undefined, displayOrder: rules.length }])
                }
              >
                {a.addRule}
              </Button>
            </div>
            <div className="space-y-3">
              {rules.map((rule, index) => (
                <RuleEditor
                  key={index}
                  rule={rule}
                  codes={codes}
                  onChange={(next) => change(fields, rules.map((current, at) => (at === index ? next : current)))}
                  onRemove={() => change(fields, rules.filter((_, at) => at !== index))}
                />
              ))}
            </div>
          </section>
        </div>
      </Card>

      {previewSchema && previewSchema.fields.length > 0 && (
        <Card>
          <h2 className="text-base font-semibold text-slate-800">{a.preview}</h2>
          <p className="mt-1 text-sm text-slate-500">{a.previewHelp}</p>
          <div className="mt-4">
            <DynamicForm schema={previewSchema} value={preview} onChange={setPreview} />
          </div>
        </Card>
      )}

      <Card>
        <h2 className="text-base font-semibold text-slate-800">{a.versions}</h2>
        <div className="mt-2 space-y-1">
          {versions.map((version) => (
            <p key={version.id} className="text-sm text-slate-600">
              <span dir="ltr">v{version.versionNumber}</span> · {version.status === 'Draft' ? a.draft : version.status}
              {version.publishedAt && ` · ${formatDateTime(version.publishedAt)}`} · {version.fieldCount} {a.fields}
            </p>
          ))}
        </div>
      </Card>

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

      <Dialog
        open={confirmPublish}
        onClose={() => setConfirmPublish(false)}
        title={a.publish}
        maxWidth="sm"
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmPublish(false)}>
              {a.cancel}
            </Button>
            <Button onClick={publish}>{a.publish}</Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">{a.publishConfirm}</p>
      </Dialog>

      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} />
    </div>
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
    <ul className="mt-1 space-y-0.5 list-disc ps-4">
      {entries.flatMap(([path, messages]) =>
        messages.map((message) => (
          <li key={`${path}-${message}`}>
            <strong>{describe(path)}</strong>: {message}
          </li>
        )),
      )}
    </ul>
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
    <Card>
      <h2 className="text-base font-semibold text-slate-800">{a.settings}</h2>
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <TextField label={a.name} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} />
        <Select
          label={a.editPolicy}
          value={form.settings.metadataEditPolicy}
          onChange={(event) =>
            setForm({
              ...form,
              settings: {
                ...form.settings,
                metadataEditPolicy: event.target.value as DocumentTypeSettings['metadataEditPolicy'],
              },
            })
          }
        >
          <option value="NewRevision">{a.policyNewRevision}</option>
          <option value="InPlace">{a.policyInPlace}</option>
        </Select>
        <TextField
          label={a.description}
          value={form.description}
          onChange={(event) => setForm({ ...form, description: event.target.value })}
          className="sm:col-span-2"
        />
        <Select
          label={a.workflowMode}
          value={form.settings.workflowMode ?? 'None'}
          onChange={(event) =>
            setForm({ ...form, settings: { ...form.settings, workflowMode: event.target.value as DocumentTypeSettings['workflowMode'] } })
          }
        >
          <option value="None">{a.modeNone}</option>
          <option value="Manual">{a.modeManual}</option>
          <option value="AutoOnVersion">{a.modeAuto}</option>
        </Select>
        <Select
          label={a.workflow}
          value={form.settings.workflowId ?? ''}
          onChange={(event) => setForm({ ...form, settings: { ...form.settings, workflowId: event.target.value || null } })}
          disabled={(form.settings.workflowMode ?? 'None') === 'None'}
        >
          <option value="">—</option>
          {(workflows.data ?? []).map((workflow) => (
            <option key={workflow.id} value={workflow.id} disabled={!workflow.latestPublishedVersionId || !workflow.isActive}>
              {workflow.name}
            </option>
          ))}
        </Select>
        <ChipsInput
          label={a.allowedExtensions}
          value={form.settings.allowedExtensions}
          onChange={(next) =>
            setForm({
              ...form,
              settings: {
                ...form.settings,
                allowedExtensions: next.map((item) => item.trim().replace(/^\./, '').toLowerCase()).filter(Boolean),
              },
            })
          }
          helperText={a.allowedExtensionsHelp}
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
        />
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <Switch label={a.active} checked={form.isActive} onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
        <Button onClick={save} disabled={busy || !form.name.trim()}>
          {a.saveSettings}
        </Button>
      </div>
      {message && <Alert className="mt-4" severity={message.ok ? 'success' : 'error'}>{message.text}</Alert>}
    </Card>
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
    <Dialog
      open
      onClose={onClose}
      title={a.editField}
      maxWidth="lg"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {a.cancel}
          </Button>
          <Button onClick={save} disabled={!draft.label.fa.trim() || !draft.code || defaultError}>
            {a.save}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label={a.label}
            value={draft.label.fa}
            onChange={(event) => set({ label: { ...draft.label, fa: event.target.value } })}
            required
          />
          <TextField
            label={a.labelEn}
            value={draft.label.en ?? ''}
            onChange={(event) => set({ label: { ...draft.label, en: event.target.value || null } })}
            dir="ltr"
          />
          <TextField
            label={a.code}
            value={draft.code}
            onChange={(event) => set({ code: event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') })}
            helperText="snake_case"
            required
            dir="ltr"
            maxLength={63}
          />
          <Select label={a.type} value={draft.type} onChange={(event) => set({ type: event.target.value as FieldType, validation: {} })}>
            {fieldTypes.map((type) => (
              <option key={type} value={type}>
                {fieldTypeLabels[type]}
              </option>
            ))}
          </Select>
        </div>

        <div className="flex flex-wrap gap-x-4 gap-y-2">
          {(
            [
              ['isRequired', a.required],
              ['isApprovalRelevant', a.approvalRelevant],
              ['isSearchable', a.searchable],
              ['showInList', a.showInList],
              ['isActive', a.fieldActive],
            ] as const
          ).map(([key, label]) => (
            <Switch key={key} label={label} checked={!!draft[key]} onChange={(event) => set({ [key]: event.target.checked })} />
          ))}
        </div>
        {draft.isApprovalRelevant && <p className="text-xs text-slate-500">{a.approvalRelevantHelp}</p>}

        <TextField
          label={a.helpText}
          value={draft.helpText?.fa ?? ''}
          onChange={(event) => set({ helpText: event.target.value ? { fa: event.target.value } : null })}
        />

        {textTypes.includes(draft.type) && (
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={a.minLength} type="number" value={draft.validation.minLength ?? ''} onChange={(event) => setValidation({ minLength: num(event.target.value) })} />
            <TextField label={a.maxLength} type="number" value={draft.validation.maxLength ?? ''} onChange={(event) => setValidation({ maxLength: num(event.target.value) })} />
            <TextField
              label={a.pattern}
              value={draft.validation.pattern ?? ''}
              onChange={(event) => setValidation({ pattern: event.target.value || null })}
              dir="ltr"
            />
            <TextField
              label={a.patternMessage}
              value={draft.validation.patternMessage?.fa ?? ''}
              onChange={(event) => setValidation({ patternMessage: event.target.value ? { fa: event.target.value } : null })}
            />
          </div>
        )}

        {numberTypes.includes(draft.type) && (
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={a.min} type="number" value={draft.validation.min ?? ''} onChange={(event) => setValidation({ min: num(event.target.value) })} />
            <TextField label={a.max} type="number" value={draft.validation.max ?? ''} onChange={(event) => setValidation({ max: num(event.target.value) })} />
            {draft.type === 'Decimal' && (
              <TextField label={a.scale} type="number" value={draft.validation.scale ?? ''} onChange={(event) => setValidation({ scale: num(event.target.value) })} />
            )}
          </div>
        )}

        {draft.type === 'Date' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={a.minDate} value={draft.validation.minDate ?? ''} onChange={(event) => setValidation({ minDate: event.target.value || null })} dir="ltr" />
            <TextField label={a.maxDate} value={draft.validation.maxDate ?? ''} onChange={(event) => setValidation({ maxDate: event.target.value || null })} dir="ltr" />
          </div>
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
          dir="ltr"
        />
      </div>
    </Dialog>
  );
}

function OptionsEditor({ options, onChange }: { options: FieldOption[]; onChange: (options: FieldOption[]) => void }) {
  const update = (index: number, patch: Partial<FieldOption>) =>
    onChange(options.map((option, at) => (at === index ? { ...option, ...patch } : option)));

  return (
    <div className="space-y-2">
      <p className="text-sm font-semibold text-slate-800">{a.options}</p>
      <div className="space-y-3">
        {options.map((option, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-2">
            <TextField
              size="sm"
              label={a.optionValue}
              value={option.value}
              onChange={(event) => update(index, { value: event.target.value })}
              dir="ltr"
            />
            <TextField size="sm" label={a.optionLabel} value={option.label.fa} onChange={(event) => update(index, { label: { ...option.label, fa: event.target.value } })} />
            <div className="flex flex-wrap items-center justify-between gap-2 sm:col-span-2">
              <Switch label={a.fieldActive} checked={option.isActive} onChange={(event) => update(index, { isActive: event.target.checked })} />
              <Button size="sm" variant="danger" onClick={() => onChange(options.filter((_, at) => at !== index))}>
                {a.remove}
              </Button>
            </div>
          </div>
        ))}
      </div>
      <Button size="sm" variant="ghost" onClick={() => onChange([...options, { value: '', label: { fa: '' }, displayOrder: options.length, isActive: true }])}>
        {a.addOption}
      </Button>
    </div>
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
    <Card className="space-y-4">
      <div className="grid items-end gap-4 sm:grid-cols-[11rem_minmax(0,1fr)_auto]">
        <Select
          label={a.ruleKind}
          value={rule.kind}
          onChange={(event) => {
            const kind = event.target.value as RuleKind;
            onChange({ ...rule, kind, assertion: kind === 'Validate' ? rule.assertion : undefined });
          }}
        >
          {(Object.keys(ruleKindLabels) as RuleKind[]).map((kind) => (
            <option key={kind} value={kind}>
              {ruleKindLabels[kind]}
            </option>
          ))}
        </Select>
        <div className="w-full">
          <label className="mb-1.5 block text-sm font-medium text-slate-700">{a.targets}</label>
          <div className="flex flex-wrap gap-x-4 gap-y-2 rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 shadow-sm">
            {codes.length === 0 && <span className="text-sm text-slate-400">—</span>}
            {codes.map((code) => (
              <Checkbox
                key={code}
                label={<span dir="ltr">{code}</span>}
                checked={rule.targets.includes(code)}
                onChange={(event) =>
                  onChange({
                    ...rule,
                    targets: event.target.checked
                      ? [...rule.targets, code]
                      : rule.targets.filter((target) => target !== code),
                  })
                }
              />
            ))}
          </div>
        </div>
        <Button variant="danger" onClick={onRemove}>
          {a.remove}
        </Button>
      </div>
      <ExpressionField label={a.condition} value={rule.condition} codes={codes} onChange={(condition) => onChange({ ...rule, condition })} />
      {rule.kind === 'Validate' && (
        <>
          <ExpressionField label={a.assertion} value={rule.assertion} codes={codes} onChange={(assertion) => onChange({ ...rule, assertion })} />
          <TextField
            label={a.message}
            value={rule.message?.fa ?? ''}
            onChange={(event) => onChange({ ...rule, message: event.target.value ? { fa: event.target.value } : null })}
          />
        </>
      )}
    </Card>
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
    <TextArea
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
      rows={2}
      dir="ltr"
      style={{ fontFamily: 'monospace' }}
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
