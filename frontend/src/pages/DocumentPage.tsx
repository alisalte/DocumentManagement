import { Alert, Button, Card, Checkbox, Chip, Dialog, ProgressBar, Select, TextArea, TextField, Toast } from '../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router';
import { DocumentViewer } from '../components/DocumentViewer';
import { FilePicker } from '../components/FilePicker';
import { SharesPanel } from '../components/sharing/SharesPanel';
import { AclEditor } from '../components/acl/AclEditor';
import { d as directory } from './admin/directoryStrings';
import { DynamicForm } from '../components/metadata/DynamicForm';
import { MetadataView } from '../components/metadata/MetadataView';
import { WorkflowPanel } from '../components/workflow/WorkflowPanel';
import { approvalLabels } from '../components/workflow/workflowStrings';
import { TagInput } from '../components/TagInput';
import { api, ApiError, type DocumentDetails, type DocumentVersion, type Metadata } from '../lib/api';
import { clientErrors, toSubmission } from '../lib/metadata';
import { formatDateTime } from '../lib/dates';
import { formatBytes, newIdempotencyKey } from '../lib/format';
import { describeError, t } from '../strings';

const can = (document: DocumentDetails | undefined, permission: string) =>
  document?.allowedActions.includes(permission) ?? false;

const noop = () => {};

export function DocumentPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>((location.state as { notice?: string } | null)?.notice ?? null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<'edit' | 'metadata' | 'version' | 'delete' | null>(null);
  const [aclOpen, setAclOpen] = useState(false);
  // Null: the viewer shows what a plain reader gets; a version row can point it elsewhere.
  const [previewVersion, setPreviewVersion] = useState<string | null>(null);

  const document = useQuery({ queryKey: ['document', id], queryFn: () => api.document(id) });
  const versions = useQuery({ queryKey: ['versions', id], queryFn: () => api.versions(id), enabled: document.isSuccess });

  // Read with the schema the current row was written against, never with a newer one.
  const schemaId = document.data?.currentSchemaVersionId;
  const schema = useQuery({
    queryKey: ['schema', schemaId],
    queryFn: () => api.schema(schemaId!),
    enabled: !!schemaId,
    staleTime: Infinity,
  });

  // The type decides whether versions go through a workflow at all.
  const types = useQuery({ queryKey: ['document-types'], queryFn: api.documentTypes });
  const workflowMode = types.data?.find((type) => type.id === document.data?.documentTypeId)?.settings?.workflowMode ?? 'None';

  const refresh = async (message: string) => {
    setDialog(null);
    setNotice(message);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['document', id] }),
      queryClient.invalidateQueries({ queryKey: ['versions', id] }),
      queryClient.invalidateQueries({ queryKey: ['documents'] }),
    ]);
  };

  const download = async (version: DocumentVersion | null) => {
    setError(null);
    try {
      await api.download(id, version?.id ?? null, version?.fileName ?? 'document');
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  if (document.isPending) {
    return <ProgressBar />;
  }

  if (document.isError) {
    return (
      <Alert severity="error" action={<Button as={RouterLink} to="/">{t.back}</Button>}>
        {describeError(document.error)}
      </Alert>
    );
  }

  const doc = document.data;
  const current = versions.data?.find((version) => version.isCurrent) ?? null;

  return (
    <div className="max-w-[1000px] space-y-4 sm:space-y-5">
      <Card>
        <div className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
            <div className="min-w-0 flex-1">
              <h1 className="text-xl font-bold break-words text-slate-800">{doc.title}</h1>
              <p className="text-sm text-slate-500">
                <RouterLink to={`/?category=${doc.categoryId}`} className="text-brand-700 hover:underline">
                  {doc.categoryName}
                </RouterLink>
                {current && <> · <span dir="ltr">{current.label}</span></>}
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              {can(doc, 'DOCUMENT_DOWNLOAD') && (
                <Button onClick={() => download(null)}>{t.download}</Button>
              )}
              {can(doc, 'DOCUMENT_CREATE_VERSION') && (
                <Button variant="outline" onClick={() => setDialog('version')}>
                  {t.addVersion}
                </Button>
              )}
              {can(doc, 'DOCUMENT_EDIT') && (
                <Button variant="ghost" onClick={() => setDialog('edit')}>
                  {t.edit}
                </Button>
              )}
              {can(doc, 'DOCUMENT_MANAGE_PERMISSION') && (
                <Button variant="ghost" onClick={() => setAclOpen(true)}>
                  {directory.permissionsTitle}
                </Button>
              )}
              {can(doc, 'DOCUMENT_DELETE') && (
                <Button variant="danger" onClick={() => setDialog('delete')}>
                  {t.delete}
                </Button>
              )}
            </div>
          </div>

          {doc.description && (
            <p className="break-words whitespace-pre-wrap text-sm text-slate-700">{doc.description}</p>
          )}

          {schema.data && schema.data.fields.length > 0 && (
            <div>
              <div className="mb-2 flex items-center gap-2">
                <h2 className="flex-1 text-base font-semibold text-slate-800">{t.metadata}</h2>
                {can(doc, 'DOCUMENT_EDIT') && (
                  <Button size="sm" variant="ghost" onClick={() => setDialog('metadata')}>
                    {t.editMetadata}
                  </Button>
                )}
              </div>
              <MetadataView schema={schema.data} metadata={doc.currentMetadata} />
            </div>
          )}

          {doc.tags.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {doc.tags.map((tag) => (
                <Chip key={tag.id} label={tag.name} size="small" />
              ))}
            </div>
          )}

          <p className="text-xs text-slate-400">
            {t.createdAt}: {formatDateTime(doc.createdAt)} · {t.updatedAt}: {formatDateTime(doc.updatedAt)}
          </p>

          {error && <Alert severity="error">{error}</Alert>}
        </div>
      </Card>

      {aclOpen && (
        <AclEditor resourceType="Document" resourceId={doc.id} resourceName={doc.title} open onClose={() => setAclOpen(false)} />
      )}

      <DocumentViewer documentId={doc.id} versionId={previewVersion} canReprocess={can(doc, 'DOCUMENT_EDIT')} />

      {workflowMode !== 'None' && (
        <WorkflowPanel
          documentId={doc.id}
          currentVersion={current}
          canStart={workflowMode === 'Manual' && can(doc, 'DOCUMENT_EDIT')}
          onChanged={setNotice}
        />
      )}

      {(can(doc, 'DOCUMENT_SHARE') || can(doc, 'DOCUMENT_SHARE_EXTERNAL') || can(doc, 'DOCUMENT_MANAGE_PERMISSION')) && (
        <SharesPanel
          documentId={doc.id}
          versions={versions.data ?? []}
          canDownload={can(doc, 'DOCUMENT_DOWNLOAD')}
          canPrint={can(doc, 'DOCUMENT_PRINT')}
          onChanged={setNotice}
        />
      )}

      <Card flush>
        {versions.isFetching && <ProgressBar />}
        <div className="px-4 pt-4 sm:px-5">
          <h2 className="text-base font-semibold text-slate-800">{t.versions}</h2>
        </div>
        <ul className="mt-3 divide-y divide-slate-100">
          {(versions.data ?? []).map((version) => (
            <VersionRow
              key={version.id}
              version={version}
              canDownload={can(doc, 'DOCUMENT_DOWNLOAD')}
              onDownload={() => download(version)}
              onPreview={() => {
                setPreviewVersion(version.isEffective ? null : version.id);
                window.scrollTo({ top: 0, behavior: 'smooth' });
              }}
            />
          ))}
        </ul>
      </Card>

      {dialog === 'edit' && (
        <EditDialog document={doc} onClose={() => setDialog(null)} onSaved={() => refresh(t.saved)} />
      )}
      {dialog === 'metadata' && schema.data && (
        <MetadataDialog
          document={doc}
          currentSchemaId={schema.data.versionId}
          onClose={() => setDialog(null)}
          onSaved={(outcome) =>
            refresh(outcome === 'revision' ? t.revisionCreated : outcome === 'in_place' ? t.updatedInPlace : t.unchanged)
          }
        />
      )}
      {dialog === 'version' && (
        <AddVersionDialog
          documentId={doc.id}
          baseVersionId={doc.currentVersionId}
          onClose={() => setDialog(null)}
          onSaved={() => refresh(t.versionAdded)}
        />
      )}
      {dialog === 'delete' && (
        <DeleteDialog
          documentId={doc.id}
          onClose={() => setDialog(null)}
          onDeleted={async () => {
            await queryClient.invalidateQueries({ queryKey: ['documents'] });
            navigate(`/?category=${doc.categoryId}`);
          }}
        />
      )}

      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} />
    </div>
  );
}

function VersionRow({
  version,
  canDownload,
  onDownload,
  onPreview,
}: {
  version: DocumentVersion;
  canDownload: boolean;
  onDownload: () => void;
  onPreview: () => void;
}) {
  const scan: Record<string, { label: string; color: 'warning' | 'error' }> = {
    Pending: { label: t.scanPending, color: 'warning' },
    Infected: { label: t.scanInfected, color: 'error' },
    Failed: { label: t.scanFailed, color: 'error' },
  };
  const blocked = scan[version.scanStatus];

  return (
    <li className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-start sm:gap-4 sm:px-5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span dir="ltr" className="font-semibold text-slate-800">
            {version.label}
          </span>
          <Chip size="small" variant="outlined" label={changeKindLabel(version.changeKind)} />
          {version.approvalStatus !== 'NotRequired' && (
            <Chip
              size="small"
              color={version.approvalStatus === 'Approved' ? 'success' : version.approvalStatus === 'Rejected' ? 'error' : 'default'}
              label={approvalLabels[version.approvalStatus] ?? version.approvalStatus}
            />
          )}
          {version.isCurrent && <Chip size="small" color="primary" label={t.current} />}
          {version.isEffective && !version.isCurrent && <Chip size="small" label={t.effective} />}
          {blocked && <Chip size="small" color={blocked.color} label={blocked.label} />}
        </div>

        <p className="mt-1 break-words text-sm text-slate-700">
          {version.fileName} · {formatBytes(version.fileSize)}
        </p>

        {version.changeDescription && <p className="text-sm text-slate-500">{version.changeDescription}</p>}

        <p className="text-xs text-slate-400">{formatDateTime(version.createdAt)}</p>

        <p dir="ltr" title={t.sha256} className="block break-all font-mono text-xs text-slate-400">
          SHA-256 {version.sha256}
        </p>
      </div>

      {!blocked && (
        <div className="flex shrink-0 gap-1 self-start">
          <Button size="sm" variant="ghost" onClick={onPreview}>
            {t.preview}
          </Button>
          {canDownload && (
            <Button size="sm" variant="ghost" onClick={onDownload}>
              {t.downloadVersion}
            </Button>
          )}
        </div>
      )}
    </li>
  );
}

function changeKindLabel(kind: string): string {
  switch (kind) {
    case 'Initial':
      return t.changeKindInitial;
    case 'Metadata':
      return t.changeKindMetadata;
    case 'ContentAndMetadata':
      return t.changeKindBoth;
    default:
      return t.changeKindContent;
  }
}

/**
 * Edits metadata without a new file. The server decides between a new revision (V3.2) and an
 * in-place update from the type's edit policy and the approval-relevant flags; the dialog only
 * reports which one happened. Offers moving to the latest schema when there is a newer one.
 */
function MetadataDialog({
  document,
  currentSchemaId,
  onClose,
  onSaved,
}: {
  document: DocumentDetails;
  currentSchemaId: string;
  onClose: () => void;
  onSaved: (outcome: string) => void;
}) {
  const latest = useQuery({
    queryKey: ['schema-latest', document.documentTypeId],
    queryFn: () => api.latestSchema(document.documentTypeId),
  });
  const [upgrade, setUpgrade] = useState(false);
  const schemaId = upgrade && latest.data ? latest.data.versionId : currentSchemaId;
  const schema = useQuery({ queryKey: ['schema', schemaId], queryFn: () => api.schema(schemaId), staleTime: Infinity });

  const [values, setValues] = useState<Metadata>(() => ({ ...(document.currentMetadata ?? {}) }));
  const [changeDescription, setChangeDescription] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const idempotencyKey = useRef(newIdempotencyKey());

  const save = async () => {
    if (!schema.data) return;
    const local = clientErrors(schema.data, values);
    setErrors(local);
    if (Object.keys(local).length > 0) return;

    setBusy(true);
    setError(null);
    try {
      const result = await api.updateMetadata(
        document.id,
        {
          metadata: toSubmission(schema.data, values),
          changeDescription: changeDescription.trim() || null,
          baseVersionId: document.currentVersionId,
          upgradeSchema: upgrade,
        },
        idempotencyKey.current,
      );
      onSaved(result.outcome);
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setErrors(caught.fieldErrors);
    } finally {
      setBusy(false);
    }
  };

  const newerSchema = latest.data && latest.data.versionId !== currentSchemaId;

  return (
    <FormDialog
      title={t.editMetadata}
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={save}
      submitLabel={t.save}
      disabled={!schema.data}
    >
      {newerSchema && (
        <Checkbox
          checked={upgrade}
          onChange={(event) => setUpgrade(event.target.checked)}
          disabled={busy}
          label={
            <span className="flex flex-col">
              <span className="text-sm text-slate-700">{t.upgradeSchema}</span>
              <span className="text-xs text-slate-500">{t.upgradeSchemaHelp}</span>
            </span>
          }
        />
      )}
      {schema.data ? (
        <DynamicForm schema={schema.data} value={values} onChange={setValues} errors={errors} disabled={busy} />
      ) : (
        <ProgressBar />
      )}
      <TextField
        label={t.changeDescription}
        value={changeDescription}
        onChange={(event) => setChangeDescription(event.target.value)}
      />
    </FormDialog>
  );
}

function FormDialog({
  title,
  busy,
  error,
  onClose,
  onSubmit,
  submitLabel,
  submitColor = 'primary',
  disabled,
  children,
}: {
  title: string;
  busy: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: () => void;
  submitLabel: string;
  submitColor?: 'primary' | 'error';
  disabled?: boolean;
  children: ReactNode;
}) {
  return (
    <Dialog
      open
      onClose={busy ? noop : onClose}
      title={title}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            {t.cancel}
          </Button>
          <Button
            variant={submitColor === 'error' ? 'danger' : 'primary'}
            onClick={onSubmit}
            disabled={busy || disabled}
          >
            {busy ? t.saving : submitLabel}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {children}
        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}

function EditDialog({
  document,
  onClose,
  onSaved,
}: {
  document: DocumentDetails;
  onClose: () => void;
  onSaved: () => void;
}) {
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const [title, setTitle] = useState(document.title);
  const [description, setDescription] = useState(document.description ?? '');
  const [categoryId, setCategoryId] = useState(document.categoryId);
  const [tags, setTags] = useState(document.tags.map((tag) => tag.name));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Moving needs DOCUMENT_CREATE in the target; the current folder is always offered.
  const targets = (categories.data ?? []).filter(
    (category) => category.canCreate || category.id === document.categoryId,
  );

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.updateDocument(document.id, {
        title: title.trim(),
        description: description.trim() || null,
        categoryId,
      });
      await api.setTags(document.id, tags);
      onSaved();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormDialog
      title={t.edit}
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={save}
      submitLabel={t.save}
      disabled={!title.trim()}
    >
      <TextField label={t.title} value={title} onChange={(event) => setTitle(event.target.value)} required />
      <Select label={t.category} value={categoryId} onChange={(event) => setCategoryId(event.target.value)}>
        {targets.map((category) => (
          <option key={category.id} value={category.id} style={{ paddingInlineStart: `${16 + category.depth * 8}px` }}>
            {category.name}
          </option>
        ))}
        {targets.every((category) => category.id !== document.categoryId) && (
          <option value={document.categoryId}>{document.categoryName}</option>
        )}
      </Select>
      <TextArea
        label={t.description}
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        rows={3}
      />
      <TagInput value={tags} onChange={setTags} disabled={busy} />
    </FormDialog>
  );
}

function AddVersionDialog({
  documentId,
  baseVersionId,
  onClose,
  onSaved,
}: {
  documentId: string;
  baseVersionId: string | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [changeDescription, setChangeDescription] = useState('');
  const [progress, setProgress] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const staged = useRef<{ file: File; uploadId: string } | null>(null);
  const idempotencyKey = useRef(newIdempotencyKey());

  const save = async () => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      if (staged.current?.file !== file) {
        setProgress(0);
        const upload = await api.upload(file, setProgress);
        staged.current = { file, uploadId: upload.uploadId };
        idempotencyKey.current = newIdempotencyKey();
      }

      setProgress(null);
      await api.addVersion(
        documentId,
        {
          uploadId: staged.current!.uploadId,
          changeDescription: changeDescription.trim() || null,
          // Section 4.11: tell the server which version this change was based on.
          baseVersionId,
        },
        idempotencyKey.current,
      );
      onSaved();
    } catch (caught) {
      setError(describeError(caught));
      setProgress(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <FormDialog
      title={t.addVersion}
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={save}
      submitLabel={t.save}
      disabled={!file}
    >
      <FilePicker file={file} onChange={setFile} progress={progress} disabled={busy} />
      <TextField
        label={t.changeDescription}
        value={changeDescription}
        onChange={(event) => setChangeDescription(event.target.value)}
      />
    </FormDialog>
  );
}

function DeleteDialog({
  documentId,
  onClose,
  onDeleted,
}: {
  documentId: string;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const remove = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.deleteDocument(documentId, reason.trim());
      onDeleted();
    } catch (caught) {
      setError(describeError(caught));
      setBusy(false);
    }
  };

  return (
    <FormDialog
      title={t.delete}
      busy={busy}
      error={error}
      onClose={onClose}
      onSubmit={remove}
      submitLabel={t.delete}
      submitColor="error"
    >
      <p className="text-sm text-slate-700">{t.deleteConfirm}</p>
      <TextField label={t.deleteReason} value={reason} onChange={(event) => setReason(event.target.value)} />
    </FormDialog>
  );
}
