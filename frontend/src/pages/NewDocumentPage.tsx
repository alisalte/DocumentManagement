import { Alert, Button, Card, Select, TextArea, TextField } from '../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { FilePicker } from '../components/FilePicker';
import { DynamicForm } from '../components/metadata/DynamicForm';
import { TagInput } from '../components/TagInput';
import { api, ApiError, type CategoryNode, type Metadata, type UploadResult } from '../lib/api';
import { clientErrors, defaultsOf, toSubmission } from '../lib/metadata';
import { newIdempotencyKey } from '../lib/format';
import { useSession } from '../session';
import { describeError, t } from '../strings';

/** Same set as scripts/grant-access.sh — enough to file and work with documents under the root. */
const filingPermissions = [
  'DOCUMENT_VIEW',
  'DOCUMENT_DOWNLOAD',
  'DOCUMENT_PRINT',
  'DOCUMENT_VIEW_DRAFT',
  'DOCUMENT_CREATE',
  'DOCUMENT_EDIT',
  'DOCUMENT_CREATE_VERSION',
  'DOCUMENT_DELETE',
  'DOCUMENT_RESTORE',
  'DOCUMENT_SHARE',
  'DOCUMENT_SHARE_EXTERNAL',
] as const;

/**
 * Two steps behind one button: the file streams to staging (with progress), then the document
 * is filed against the staged upload. A retry after a dropped connection reuses both the staged
 * upload and the Idempotency-Key, so it can never file the same document twice.
 */
export function NewDocumentPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user } = useSession();
  const [params] = useSearchParams();

  const categories = useQuery({
    queryKey: ['categories'],
    queryFn: api.categories,
    // ACL grants change canCreate; never keep a stale empty list after the admin editor.
    refetchOnMount: 'always',
  });
  const types = useQuery({ queryKey: ['document-types'], queryFn: api.documentTypes });

  const creatable = useMemo(
    () => (categories.data ?? []).filter((category) => category.canCreate),
    [categories.data],
  );
  const canManageAcl =
    !!user && (user.isSystemAdmin || user.systemPermissions.includes('ADMIN_MANAGE_CATEGORIES'));

  const [categoryId, setCategoryId] = useState(params.get('category') ?? '');
  const [documentTypeId, setDocumentTypeId] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [tags, setTags] = useState<string[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [duplicates, setDuplicates] = useState<UploadResult['duplicates']>([]);
  const [metadata, setMetadata] = useState<Metadata>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  const staged = useRef<{ file: File; upload: UploadResult } | null>(null);
  const idempotencyKey = useRef(newIdempotencyKey());

  const selectedCategory = creatable.some((category) => category.id === categoryId) ? categoryId : '';
  const selectedType = documentTypeId || types.data?.find((type) => type.code === 'GENERAL')?.id || '';

  // The form is the latest published schema of the chosen type.
  const schema = useQuery({
    queryKey: ['schema-latest', selectedType],
    queryFn: () => api.latestSchema(selectedType),
    enabled: !!selectedType,
  });

  useEffect(() => {
    if (schema.data) {
      setMetadata(defaultsOf(schema.data));
      setFieldErrors({});
    }
  }, [schema.data]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!file || !selectedCategory || !selectedType || !schema.data) return;

    const local = clientErrors(schema.data, metadata);
    setFieldErrors(local);
    if (Object.keys(local).length > 0) return;

    setBusy(true);
    setError(null);
    try {
      if (staged.current?.file !== file) {
        setProgress(0);
        const upload = await api.upload(file, setProgress);
        staged.current = { file, upload };
        idempotencyKey.current = newIdempotencyKey();
        setDuplicates(upload.duplicates);
      }

      setProgress(null);
      const created = await api.createDocument(
        {
          title: title.trim(),
          description: description.trim() || null,
          categoryId: selectedCategory,
          documentTypeId: selectedType,
          uploadId: staged.current!.upload.uploadId,
          tags,
          changeDescription: null,
          metadata: toSubmission(schema.data, metadata),
        },
        idempotencyKey.current,
      );

      await queryClient.invalidateQueries({ queryKey: ['documents'] });
      navigate(`/documents/${created.documentId}`, { state: { notice: t.created } });
    } catch (caught) {
      setError(describeError(caught));
      if (caught instanceof ApiError) setFieldErrors(caught.fieldErrors);
      setProgress(null);
    } finally {
      setBusy(false);
    }
  }

  if (categories.isSuccess && creatable.length === 0) {
    return (
      <NoCreatableCategory
        categories={categories.data ?? []}
        canManageAcl={canManageAcl}
        userId={user?.id}
        onGranted={async () => {
          await queryClient.refetchQueries({ queryKey: ['categories'] });
        }}
      />
    );
  }

  return (
    <div className="mx-auto w-full max-w-[720px] space-y-5">
      <form onSubmit={submit}>
        <Card className="space-y-4">
          <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.newDocument}</h1>

          <FilePicker
            file={file}
            onChange={(next) => {
              setFile(next);
              setDuplicates([]);
              if (next && !title) setTitle(next.name.replace(/\.[^.]+$/, ''));
            }}
            progress={progress}
            disabled={busy}
          />

          {duplicates.length > 0 && (
            <Alert severity="warning">
              {t.duplicateNotice}{' '}
              {duplicates.map((duplicate, index) => (
                <span key={duplicate.documentId}>
                  {index > 0 && '، '}
                  <RouterLink to={`/documents/${duplicate.documentId}`} className="text-ink-700 hover:underline">
                    {duplicate.title}
                  </RouterLink>
                </span>
              ))}
            </Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <TextField
              label={t.title}
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              required
              maxLength={500}
              className="sm:col-span-2"
            />

            <Select
              label={t.category}
              value={selectedCategory}
              onChange={(event) => setCategoryId(event.target.value)}
              required
            >
              {creatable.map((category) => (
                <option
                  key={category.id}
                  value={category.id}
                  style={{ paddingInlineStart: `${16 + category.depth * 8}px` }}
                >
                  {category.name}
                </option>
              ))}
            </Select>

            <Select
              label={t.documentType}
              value={selectedType}
              onChange={(event) => setDocumentTypeId(event.target.value)}
              required
            >
              {(types.data ?? []).map((type) => (
                <option key={type.id} value={type.id} disabled={!type.latestPublishedVersionId}>
                  {type.name}
                </option>
              ))}
            </Select>

            <TextArea
              label={t.description}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              rows={3}
              maxLength={4000}
              className="sm:col-span-2"
            />
          </div>

          <TagInput value={tags} onChange={setTags} disabled={busy} />

          {schema.data && schema.data.fields.length > 0 && (
            <div className="space-y-3 pt-1">
              <h2 className="text-base font-semibold text-ink-800">{t.metadata}</h2>
              <DynamicForm
                schema={schema.data}
                value={metadata}
                onChange={setMetadata}
                errors={fieldErrors}
                disabled={busy}
              />
            </div>
          )}

          {error && <Alert severity="error">{error}</Alert>}

          <div className="flex flex-wrap justify-end gap-2 pt-1">
            <Button variant="ghost" onClick={() => navigate(-1)} disabled={busy}>
              {t.cancel}
            </Button>
            <Button
              type="submit"
              loading={busy}
              disabled={busy || !file || !selectedCategory || !selectedType || !title.trim()}
            >
              {busy ? t.saving : t.submit}
            </Button>
          </div>
        </Card>
      </form>
    </div>
  );
}

/**
 * Filing needs DOCUMENT_CREATE on a category. System administrators deliberately do not get that
 * from is_system_admin (decision D5); they grant it through the ACL — here, with one click on the
 * root, or via the category permissions editor.
 */
function NoCreatableCategory({
  categories,
  canManageAcl,
  userId,
  onGranted,
}: {
  categories: CategoryNode[];
  canManageAcl: boolean;
  userId: string | undefined;
  onGranted: () => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const root = categories.find((category) => category.parentId === null) ?? categories[0];

  const grantMyself = async () => {
    if (!userId || !root) return;
    setBusy(true);
    setError(null);
    try {
      const existing = await api.acl.list('Category', root.id, false);
      const mine = new Set(
        existing
          .filter((entry) => entry.subjectType === 'User' && entry.subjectId === userId)
          .map((entry) => entry.permissionCode),
      );
      for (const permission of filingPermissions) {
        if (mine.has(permission)) continue;
        await api.acl.grant('Category', root.id, {
          subjectType: 'User',
          subjectId: userId,
          permissionCode: permission,
          effect: 'Allow',
          inherit: true,
          reason: 'self-grant from new document page',
        });
      }
      setDone(true);
      await onGranted();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto w-full max-w-[640px] space-y-4 page-enter">
      <Card className="space-y-4">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.newDocument}</h1>
        <Alert severity="info">
          <p className="font-medium">{t.noCreatableCategory}</p>
          <p className="mt-1 text-sm opacity-90">{t.noCreatableCategoryHelp}</p>
          {canManageAcl && <p className="mt-1 text-sm opacity-90">{t.noCreatableCategoryAdminHint}</p>}
        </Alert>
        {done && <Alert severity="success">{t.grantMyselfCreateDone}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        <div className="flex flex-wrap gap-2">
          {canManageAcl && userId && root && !done && (
            <Button loading={busy} onClick={grantMyself}>
              {t.grantMyselfCreate}
            </Button>
          )}
          {canManageAcl && (
            <Button variant="outline" as={RouterLink} to="/admin/categories">
              {t.openCategoriesAdmin}
            </Button>
          )}
          <Button variant="ghost" as={RouterLink} to="/">
            {t.back}
          </Button>
        </div>
      </Card>
    </div>
  );
}
