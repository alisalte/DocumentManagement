import { Alert, Box, Button, CircularProgress, LinearProgress, Paper, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api, ApiError, type PreviewInfo } from '../lib/api';
import { describeError, t } from '../strings';

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
    <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1, flexWrap: 'wrap', rowGap: 1 }}>
        <Typography variant="h6" component="h2" sx={{ flexGrow: 1 }}>
          {t.preview}
          {info && (
            <Typography component="span" variant="body2" color="text.secondary" dir="ltr" sx={{ mx: 1 }}>
              {info.label}
            </Typography>
          )}
        </Typography>
        {info?.status === 'Ready' && info.canPrint && (
          <Button size="small" variant="outlined" onClick={print} disabled={printing}>
            {printing ? t.preparingPrint : t.print}
          </Button>
        )}
      </Stack>

      {preview.isLoading && <LinearProgress />}
      {preview.isError && (
        <Alert severity={scanBlocked ? 'warning' : 'error'}>{scanBlocked ? t.previewScan : describeError(preview.error)}</Alert>
      )}
      {message && <Alert severity="info" sx={{ mb: 1 }}>{message}</Alert>}

      {info?.status === 'Pending' && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', py: 2 }}>
          <CircularProgress size={20} />
          <Typography color="text.secondary">{t.previewPending}</Typography>
        </Stack>
      )}
      {info?.status === 'NotSupported' && <Alert severity="info">{t.previewNotSupported}</Alert>}
      {info?.status === 'Failed' && (
        <Alert
          severity="warning"
          action={canReprocess ? <Button color="inherit" size="small" onClick={reprocess}>{t.reprocess}</Button> : undefined}
        >
          {t.previewFailed}
        </Alert>
      )}

      {info?.status === 'Ready' && info.pageCount > 0 && (
        <Stack spacing={1}>
          <Box
            sx={{
              bgcolor: 'grey.100',
              borderRadius: 1,
              minHeight: 240,
              display: 'grid',
              placeItems: 'center',
              overflow: 'hidden',
            }}
          >
            {image.isLoading && <CircularProgress />}
            {image.isError && <Alert severity="error">{describeError(image.error)}</Alert>}
            {image.data && (
              <Box
                component="img"
                src={image.data}
                alt={`${t.page} ${page}`}
                // Right-click "save image" would still give only a watermarked page image.
                onContextMenu={(event) => event.preventDefault()}
                sx={{ width: '100%', height: 'auto', display: 'block', userSelect: 'none' }}
              />
            )}
          </Box>
          {info.pageCount > 1 && (
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'center' }}>
              <Button size="small" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                {t.previous}
              </Button>
              <Typography variant="body2">
                {t.page} {page.toLocaleString('fa-IR')} {t.of} {info.pageCount.toLocaleString('fa-IR')}
              </Typography>
              <Button size="small" disabled={page >= info.pageCount} onClick={() => setPage(page + 1)}>
                {t.next}
              </Button>
            </Stack>
          )}
        </Stack>
      )}
    </Paper>
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
