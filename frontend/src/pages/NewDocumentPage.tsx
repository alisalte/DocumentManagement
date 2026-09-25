import { Alert, Button, Card, Select, TextArea, TextField } from '../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { FilePicker } from '../components/FilePicker';
import { DynamicForm } from '../components/metadata/DynamicForm';
import { TagInput } from '../components/TagInput';
import { api, ApiError, type Metadata, type UploadResult } from '../lib/api';
import { clientErrors, defaultsOf, toSubmission } from '../lib/metadata';
import { newIdempotencyKey } from '../lib/format';
import { describeError, t } from '../strings';

/**
 * Two steps behind one button: the file streams to staging (with progress), then the document
 * is filed against the staged upload. A retry after a dropped connection reuses both the staged
 * upload and the Idempotency-Key, so it can never file the same document twice.
 */
export function NewDocumentPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const types = useQuery({ queryKey: ['document-types'], queryFn: api.documentTypes });

  const creatable = useMemo(
    () => (categories.data ?? []).filter((category) => category.canCreate),
    [categories.data],
  );

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
    return <Alert severity="info">{t.noCreatableCategory}</Alert>;
  }

  return (
    <div className="mx-auto w-full max-w-[720px] space-y-4 sm:space-y-5">
      <form onSubmit={submit}>
        <Card className="space-y-4">
          <h1 className="text-xl font-bold text-slate-800">{t.newDocument}</h1>

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
                  <RouterLink to={`/documents/${duplicate.documentId}`} className="text-brand-700 hover:underline">
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
              <h2 className="text-base font-semibold text-slate-800">{t.metadata}</h2>
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
