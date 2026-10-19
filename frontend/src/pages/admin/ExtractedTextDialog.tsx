import { Alert, Button, Chip, Dialog, ProgressBar, TextArea } from '../../components/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { api } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { describeError, t } from '../../strings';

const methodLabels: Record<string, string> = {
  Ocr: 'OCR',
  TextLayer: 'لایهٔ متن',
  Manual: 'دستی',
  None: 'بدون متن',
};

/**
 * The text OCR (or the file's text layer) produced for one file, with a correction in place.
 * The server decides access: VIEW on a document using the file to read, EDIT to save (D5).
 */
export function ExtractedTextDialog({
  storageObjectId,
  name,
  onClose,
}: {
  storageObjectId: string | null;
  name: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const open = storageObjectId !== null;
  const text = useQuery({
    queryKey: ['extracted-text', storageObjectId],
    queryFn: () => api.searchAdmin.text(storageObjectId!),
    enabled: open,
    retry: false,
    gcTime: 0,
    // A refetch would overwrite what the person is typing.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
  const [draft, setDraft] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    if (text.data) setDraft(text.data.text);
  }, [text.data]);

  useEffect(() => {
    if (!open) {
      setMessage(null);
      setDraft('');
    }
  }, [open]);

  const data = text.data;
  const dirty = !!data && draft !== data.text;

  const close = () => {
    if (dirty && !window.confirm(t.ocrTextChanged)) return;
    onClose();
  };

  const save = async () => {
    if (!storageObjectId) return;
    setSaving(true);
    setMessage(null);
    try {
      const saved = await api.searchAdmin.saveText(storageObjectId, draft);
      queryClient.setQueryData(['extracted-text', storageObjectId], saved);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['search-extractions'] }),
        queryClient.invalidateQueries({ queryKey: ['search-status'] }),
      ]);
      setMessage({ severity: 'success', text: t.ocrTextSaved });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      maxWidth="xl"
      title={`${t.ocrTextTitle}: ${data?.title ?? name}`}
      footer={
        <>
          <Button variant="outline" onClick={close}>
            {dirty ? t.cancel : 'بستن'}
          </Button>
          {data?.canEdit && (
            <Button onClick={save} disabled={saving || !dirty}>
              {t.save}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3">
        {text.isLoading && <ProgressBar className="rounded-full" />}
        {text.isError && <Alert severity="warning">{describeError(text.error)}</Alert>}
        {message && <Alert severity={message.severity}>{message.text}</Alert>}

        {data && (
          <>
            <div className="flex flex-wrap items-center gap-2 text-xs text-paper-500">
              <Chip size="small" color={data.method === 'Ocr' ? 'primary' : 'default'} label={methodLabels[data.method] ?? data.method} />
              {data.editedAt && <Chip size="small" variant="outlined" label={`${t.ocrTextEdited} · ${formatDateTime(data.editedAt)}`} />}
              {data.fileName && <span className="truncate">{data.fileName}</span>}
              <RouterLink to={`/documents/${data.documentId}`} className="text-ink-700 hover:underline">
                {data.title}
              </RouterLink>
            </div>

            <p className="text-sm text-paper-600">{data.canEdit ? t.ocrTextHelp : t.ocrTextReadOnly}</p>
            {!data.text && !data.canEdit && <Alert severity="info">{t.ocrTextEmpty}</Alert>}

            <TextArea
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              readOnly={!data.canEdit}
              rows={18}
              dir="auto"
              aria-label={t.ocrTextTitle}
              placeholder={data.canEdit ? t.ocrTextPlaceholder : undefined}
              className="[&_textarea]:font-[inherit] [&_textarea]:text-sm"
            />
            <p className="text-xs text-paper-500">
              {formatNumber(draft.length)} نویسه
              {dirty && ` (پیش از این ${formatNumber(data.text.length)})`}
            </p>
          </>
        )}
      </div>
    </Dialog>
  );
}
