import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
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
import { FilePicker } from '../components/FilePicker';
import { TagInput } from '../components/TagInput';
import { api, type DocumentDetails, type DocumentVersion } from '../lib/api';
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
  const [dialog, setDialog] = useState<'edit' | 'version' | 'delete' | null>(null);

  const document = useQuery({ queryKey: ['document', id], queryFn: () => api.document(id) });
  const versions = useQuery({ queryKey: ['versions', id], queryFn: () => api.versions(id), enabled: document.isSuccess });

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
              />
            </Box>
          ))}
        </List>
      </Paper>

      {dialog === 'edit' && (
        <EditDialog document={doc} onClose={() => setDialog(null)} onSaved={() => refresh(t.saved)} />
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
}: {
  version: DocumentVersion;
  canDownload: boolean;
  onDownload: () => void;
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
        {canDownload && !blocked && (
          <Box sx={{ flexShrink: 0 }}>
            <Button size="small" onClick={onDownload}>
              {t.downloadVersion}
            </Button>
          </Box>
        )}
      </Stack>
    </ListItem>
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
