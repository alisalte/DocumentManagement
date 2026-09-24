import {
  Alert,
  Box,
  Button,
  Chip,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  LinearProgress,
  List,
  ListItem,
  MenuItem,
  Paper,
  Snackbar,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router';
import { DocumentViewer } from '../components/DocumentViewer';
import { FilePicker } from '../components/FilePicker';
import { SharesPanel } from '../components/sharing/SharesPanel';
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

export function DocumentPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>((location.state as { notice?: string } | null)?.notice ?? null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<'edit' | 'metadata' | 'version' | 'delete' | null>(null);
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
    return <LinearProgress />;
  }

  if (document.isError) {
    return (
      <Alert severity="error" action={<Button component={RouterLink} to="/">{t.back}</Button>}>
        {describeError(document.error)}
      </Alert>
    );
  }

  const doc = document.data;
  const current = versions.data?.find((version) => version.isCurrent) ?? null;

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Stack spacing={1.5}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'flex-start' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="h5" component="h1" sx={{ overflowWrap: 'anywhere' }}>
                {doc.title}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                <RouterLink to={`/?category=${doc.categoryId}`}>{doc.categoryName}</RouterLink>
                {current && <> · <span dir="ltr">{current.label}</span></>}
              </Typography>
            </Box>
            <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
              {can(doc, 'DOCUMENT_DOWNLOAD') && (
                <Button variant="contained" onClick={() => download(null)}>
                  {t.download}
                </Button>
              )}
              {can(doc, 'DOCUMENT_CREATE_VERSION') && (
                <Button variant="outlined" onClick={() => setDialog('version')}>
                  {t.addVersion}
                </Button>
              )}
              {can(doc, 'DOCUMENT_EDIT') && (
                <Button onClick={() => setDialog('edit')}>{t.edit}</Button>
              )}
              {can(doc, 'DOCUMENT_DELETE') && (
                <Button color="error" onClick={() => setDialog('delete')}>
                  {t.delete}
                </Button>
              )}
            </Stack>
          </Stack>

          {doc.description && (
            <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{doc.description}</Typography>
          )}

          {schema.data && schema.data.fields.length > 0 && (
            <Box>
              <Stack direction="row" sx={{ alignItems: 'center', mb: 1 }}>
                <Typography variant="subtitle1" component="h2" sx={{ flexGrow: 1 }}>
                  {t.metadata}
                </Typography>
                {can(doc, 'DOCUMENT_EDIT') && (
                  <Button size="small" onClick={() => setDialog('metadata')}>
                    {t.editMetadata}
                  </Button>
                )}
              </Stack>
              <MetadataView schema={schema.data} metadata={doc.currentMetadata} />
            </Box>
          )}

          {doc.tags.length > 0 && (
            <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
              {doc.tags.map((tag) => (
                <Chip key={tag.id} label={tag.name} size="small" />
              ))}
            </Stack>
          )}

          <Typography variant="caption" color="text.secondary">
            {t.createdAt}: {formatDateTime(doc.createdAt)} · {t.updatedAt}: {formatDateTime(doc.updatedAt)}
          </Typography>

          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </Paper>

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

      <Paper variant="outlined">
        <Typography variant="h6" component="h2" sx={{ px: { xs: 2, sm: 3 }, pt: 2 }}>
          {t.versions}
        </Typography>
        {versions.isFetching && <LinearProgress />}
        <List>
          {(versions.data ?? []).map((version, index) => (
            <Box key={version.id}>
              {index > 0 && <Divider component="li" />}
              <VersionRow
                version={version}
                canDownload={can(doc, 'DOCUMENT_DOWNLOAD')}
                onDownload={() => download(version)}
                onPreview={() => {
                  setPreviewVersion(version.isEffective ? null : version.id);
                  window.scrollTo({ top: 0, behavior: 'smooth' });
                }}
              />
            </Box>
          ))}
        </List>
      </Paper>

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

      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
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
    <ListItem sx={{ px: { xs: 2, sm: 3 }, alignItems: 'flex-start' }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ width: '100%' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
            <Typography sx={{ fontWeight: 600 }} dir="ltr">
              {version.label}
            </Typography>
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
          </Stack>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            {version.fileName} · {formatBytes(version.fileSize)}
          </Typography>
          {version.changeDescription && (
            <Typography variant="body2" color="text.secondary">
              {version.changeDescription}
            </Typography>
          )}
          <Typography variant="caption" color="text.secondary" component="div">
            {formatDateTime(version.createdAt)}
          </Typography>
          <Typography
            variant="caption"
            color="text.secondary"
            dir="ltr"
            title={t.sha256}
            sx={{ fontFamily: 'monospace', overflowWrap: 'anywhere', display: 'block' }}
          >
            SHA-256 {version.sha256}
          </Typography>
        </Box>
        {!blocked && (
          <Stack direction="row" spacing={0.5} sx={{ flexShrink: 0 }}>
            <Button size="small" onClick={onPreview}>
              {t.preview}
            </Button>
            {canDownload && (
              <Button size="small" onClick={onDownload}>
                {t.downloadVersion}
              </Button>
            )}
          </Stack>
        )}
      </Stack>
    </ListItem>
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
        <FormControlLabel
          control={<Checkbox checked={upgrade} onChange={(event) => setUpgrade(event.target.checked)} disabled={busy} />}
          label={
            <Box>
              <Typography variant="body2">{t.upgradeSchema}</Typography>
              <Typography variant="caption" color="text.secondary">
                {t.upgradeSchemaHelp}
              </Typography>
            </Box>
          }
        />
      )}
      {schema.data ? (
        <DynamicForm schema={schema.data} value={values} onChange={setValues} errors={errors} disabled={busy} />
      ) : (
        <LinearProgress />
      )}
      <TextField
        label={t.changeDescription}
        value={changeDescription}
        onChange={(event) => setChangeDescription(event.target.value)}
        fullWidth
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
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {children}
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {t.cancel}
        </Button>
        <Button variant="contained" color={submitColor} onClick={onSubmit} disabled={busy || disabled}>
          {busy ? t.saving : submitLabel}
        </Button>
      </DialogActions>
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
      <TextField label={t.title} value={title} onChange={(event) => setTitle(event.target.value)} required fullWidth />
      <TextField
        select
        label={t.category}
        value={categoryId}
        onChange={(event) => setCategoryId(event.target.value)}
        fullWidth
      >
        {targets.map((category) => (
          <MenuItem key={category.id} value={category.id} sx={{ paddingInlineStart: 2 + category.depth }}>
            {category.name}
          </MenuItem>
        ))}
        {targets.every((category) => category.id !== document.categoryId) && (
          <MenuItem value={document.categoryId}>{document.categoryName}</MenuItem>
        )}
      </TextField>
      <TextField
        label={t.description}
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        multiline
        minRows={3}
        fullWidth
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
        fullWidth
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
      <Typography>{t.deleteConfirm}</Typography>
      <TextField label={t.deleteReason} value={reason} onChange={(event) => setReason(event.target.value)} fullWidth />
    </FormDialog>
  );
}
