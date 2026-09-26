import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api, ApiError, type PreviewInfo } from '../lib/api';
import { describeError, t } from '../strings';
import { Alert, Button, Card, ProgressBar, Spinner } from './ui';

/**
 * The in-app viewer (section 7.4): page images rendered by the server, watermarked with the
 * viewer's name, never the original file. Opening it is one audited DOCUMENT_VIEWED; the pages
 * themselves are fetched one at a time as the reader moves through them.
 */
export function DocumentViewer({
  documentId,
  versionId,
  canReprocess,
}: {
  documentId: string;
  /** Null: the version a plain reader gets (the effective one). */
  versionId: string | null;
  canReprocess: boolean;
}) {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [printing, setPrinting] = useState(false);

  const preview = useQuery({
    queryKey: ['preview', documentId, versionId],
    queryFn: () => api.preview.open(documentId, versionId),
    // Pages are rendered in the background after upload; look again until they are ready.
    refetchInterval: (query) => (query.state.data?.status === 'Pending' ? 5000 : false),
    staleTime: Infinity,
  });

  useEffect(() => setPage(1), [versionId]);

  const info = preview.data;
  const image = useQuery({
    queryKey: ['preview-page', documentId, info?.versionId, page],
    queryFn: () => api.preview.page(documentId, info!.versionId, page),
    enabled: info?.status === 'Ready' && info.pageCount > 0,
    staleTime: Infinity,
    gcTime: 60_000,
  });

  // Object URLs hold the image in memory until revoked.
  useEffect(() => {
    const url = image.data;
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
  }, [image.data]);

  const reprocess = async () => {
    if (!info) return;
    try {
      await api.preview.reprocess(documentId, info.versionId);
      setMessage(t.reprocessQueued);
      await queryClient.invalidateQueries({ queryKey: ['preview', documentId] });
    } catch (caught) {
      setMessage(describeError(caught));
    }
  };

  const print = async () => {
    if (!info) return;
    setPrinting(true);
    setMessage(null);
    try {
      await printPages(documentId, info);
    } catch (caught) {
      setMessage(describeError(caught));
    } finally {
      setPrinting(false);
    }
  };

  const scanBlocked =
    preview.error instanceof ApiError && preview.error.status === 403 && preview.error.code === 'auth.forbidden';

  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-center gap-x-3 gap-y-2">
        <h2 className="min-w-0 flex-1 text-base font-semibold text-ink-800">
          {t.preview}
          {info && (
            <span dir="ltr" className="ms-2 text-sm font-normal text-paper-500">
              {info.label}
            </span>
          )}
        </h2>
        {info?.status === 'Ready' && info.canPrint && (
          <Button size="sm" variant="outline" onClick={print} disabled={printing}>
            {printing ? t.preparingPrint : t.print}
          </Button>
        )}
      </div>

      {preview.isLoading && <ProgressBar className="mb-3" />}
      {preview.isError && (
        <Alert severity={scanBlocked ? 'warning' : 'error'} className="mb-3">
          {scanBlocked ? t.previewScan : describeError(preview.error)}
        </Alert>
      )}
      {message && (
        <Alert severity="info" className="mb-3">
          {message}
        </Alert>
      )}

      {info?.status === 'Pending' && (
        <div className="flex items-center gap-2 py-3 text-sm text-paper-500">
          <Spinner size="sm" />
          <span>{t.previewPending}</span>
        </div>
      )}
      {info?.status === 'NotSupported' && <Alert severity="info">{t.previewNotSupported}</Alert>}
      {info?.status === 'Failed' && (
        <Alert
          severity="warning"
          action={
            canReprocess ? (
              <Button size="sm" variant="outline" onClick={reprocess}>
                {t.reprocess}
              </Button>
            ) : undefined
          }
        >
          {t.previewFailed}
        </Alert>
      )}

      {info?.status === 'Ready' && info.pageCount > 0 && (
        <div className="space-y-3">
          <div className="grid min-h-60 place-items-center overflow-hidden rounded-lg bg-paper-100">
            {image.isLoading && <Spinner size="lg" />}
            {image.isError && <Alert severity="error">{describeError(image.error)}</Alert>}
            {image.data && (
              <img
                src={image.data}
                alt={`${t.page} ${page}`}
                // Right-click "save image" would still give only a watermarked page image.
                onContextMenu={(event) => event.preventDefault()}
                className="block w-full select-none"
              />
            )}
          </div>
          {info.pageCount > 1 && (
            <div className="flex flex-wrap items-center justify-center gap-3">
              <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                {t.previous}
              </Button>
              <span className="text-sm text-ink-800">
                {t.page} {page.toLocaleString('fa-IR')} {t.of} {info.pageCount.toLocaleString('fa-IR')}
              </span>
              <Button size="sm" variant="outline" disabled={page >= info.pageCount} onClick={() => setPage(page + 1)}>
                {t.next}
              </Button>
            </div>
          )}
        </div>
      )}
    </Card>
  );
}

/**
 * Prints through a hidden frame holding the print renditions: the browser's print dialog, but
 * never the original file. The server records DOCUMENT_PRINTED before the first page is sent.
 */
async function printPages(documentId: string, info: PreviewInfo): Promise<void> {
  const started = await api.preview.startPrint(documentId, info.versionId);
  await printImages(started.pageCount, (page) => api.preview.page(documentId, info.versionId, page, 'print'));
}

/** Prints page images (object URLs from <paramref name="fetchPage"/>) through a hidden frame. */
export async function printImages(pageCount: number, fetchPage: (page: number) => Promise<string>): Promise<void> {
  const urls: string[] = [];
  const frame = document.createElement('iframe');
  frame.style.position = 'fixed';
  frame.style.width = '0';
  frame.style.height = '0';
  frame.style.border = '0';
  frame.setAttribute('aria-hidden', 'true');

  try {
    for (let page = 1; page <= pageCount; page++) {
      urls.push(await fetchPage(page));
    }

    document.body.appendChild(frame);
    const target = frame.contentDocument!;
    target.open();
    target.write(
      '<!doctype html><html><head><style>@page{margin:0}body{margin:0}img{width:100%;page-break-after:always;display:block}</style></head><body></body></html>',
    );
    target.close();

    await Promise.all(
      urls.map(
        (url) =>
          new Promise<void>((resolve) => {
            const img = target.createElement('img');
            img.onload = () => resolve();
            img.onerror = () => resolve();
            img.src = url;
            target.body.appendChild(img);
          }),
      ),
    );

    frame.contentWindow!.focus();
    frame.contentWindow!.print();
  } finally {
    // Give the print dialog time to take its copy before the images go.
    setTimeout(() => {
      urls.forEach((url) => URL.revokeObjectURL(url));
      frame.remove();
    }, 60_000);
  }
}
