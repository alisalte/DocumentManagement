import { Alert, Box, Button, CircularProgress, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useParams } from 'react-router';
import { printImages } from '../components/DocumentViewer';
import { s } from '../components/sharing/sharingStrings';
import { api, ApiError, type OpenedLink } from '../lib/api';
import { formatBytes, formatNumber } from '../lib/format';
import { describeError, t } from '../strings';

/**
 * What someone outside the organisation sees when they open a shared link: no sign-in, one
 * version, page images watermarked with the link, and the file only if the link includes it.
 * Opening is an explicit click, because each opening counts against the link's limit.
 */
export function PublicLinkPage() {
  const { token = '' } = useParams();
  const [password, setPassword] = useState('');
  const [opened, setOpenedState] = useState<OpenedLink | null>(() => restoreOpened(token));
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const setOpened = (link: OpenedLink | null) => {
    rememberOpened(token, link);
    setOpenedState(link);
  };

  // Only needed before opening; a resumed session already knows everything.
  const info = useQuery({
    queryKey: ['public-link', token],
    queryFn: () => api.publicLink.info(token),
    retry: false,
    enabled: !opened,
  });

  const open = async (event?: FormEvent) => {
    event?.preventDefault();
    setOpening(true);
    setError(null);
    try {
      setOpened(await api.publicLink.open(token, password || null));
      setPassword('');
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setOpening(false);
    }
  };

  // The session ended (or the link was revoked meanwhile): back to the start.
  const ended = (caught: unknown) => {
    setOpened(null);
    setError(describeError(caught));
  };

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'grey.50', py: { xs: 2, sm: 4 } }}>
      <Container maxWidth="md">
        <Stack spacing={2}>
          <Typography variant="h6" component="p" color="text.secondary">
            {t.appTitle}
          </Typography>

          {!opened && info.isPending && <CircularProgress />}
          {!opened && info.isError && <Alert severity="error">{describeError(info.error)}</Alert>}

          {info.data && !opened && (
            <Paper variant="outlined" component="form" onSubmit={open} sx={{ p: { xs: 2, sm: 3 } }}>
              <Stack spacing={2}>
                <Typography variant="h5" component="h1">
                  {s.linkTitle}
                </Typography>
                {info.data.requiresPassword && (
                  <>
                    <Typography>{s.linkNeedsPassword}</Typography>
                    <TextField
                      type="password"
                      label={t.password}
                      value={password}
                      onChange={(event) => setPassword(event.target.value)}
                      autoComplete="off"
                      required
                      fullWidth
                    />
                  </>
                )}
                {info.data.lockedUntil && <Alert severity="warning">{s.linkLocked}</Alert>}
                {error && <Alert severity="error">{error}</Alert>}
                <Typography variant="body2" color="text.secondary">
                  {s.linkOpenHelp}
                </Typography>
                <Button type="submit" variant="contained" disabled={opening || (info.data.requiresPassword && !password)}>
                  {opening ? t.loading : s.linkOpen}
                </Button>
              </Stack>
            </Paper>
          )}

          {opened && <OpenedLinkView token={token} link={opened} onEnded={ended} />}
        </Stack>
      </Container>
    </Box>
  );
}

/**
 * An opened link is kept for the tab's lifetime, so reloading the page resumes the session instead
 * of spending another opening (a link limited to one opening would otherwise die on a refresh).
 * The server still decides: an ended or revoked session sends the visitor back to the start.
 */
const openedKey = (token: string) => `dms.link.${token}`;

function restoreOpened(token: string): OpenedLink | null {
  try {
    const stored = sessionStorage.getItem(openedKey(token));
    const link = stored ? (JSON.parse(stored) as OpenedLink) : null;
    return link && new Date(link.sessionExpiresAt).getTime() > Date.now() ? link : null;
  } catch {
    return null;
  }
}

function rememberOpened(token: string, link: OpenedLink | null) {
  try {
    if (link) sessionStorage.setItem(openedKey(token), JSON.stringify(link));
    else sessionStorage.removeItem(openedKey(token));
  } catch {
    // Storage unavailable (private mode): the session simply does not survive a reload.
  }
}

function OpenedLinkView({ token, link, onEnded }: { token: string; link: OpenedLink; onEnded: (error: unknown) => void }) {
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const session = link.sessionToken;
  const ready = link.previewStatus === 'Ready' && link.pageCount > 0;

  const image = useQuery({
    queryKey: ['public-link-page', token, session, page],
    queryFn: () => api.publicLink.page(token, session, page),
    enabled: ready,
    staleTime: Infinity,
    gcTime: 60_000,
    retry: false,
  });

  useEffect(() => {
    const url = image.data;
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
  }, [image.data]);

  useEffect(() => {
    if (image.error instanceof ApiError && (image.error.status === 401 || image.error.status === 404)) {
      onEnded(image.error);
    }
  }, [image.error, onEnded]);

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (caught) {
      if (caught instanceof ApiError && (caught.status === 401 || caught.status === 404)) {
        onEnded(caught);
      } else {
        setError(describeError(caught));
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 3 } }}>
      <Stack spacing={1.5}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'flex-start' } }}>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="h5" component="h1" sx={{ overflowWrap: 'anywhere' }}>
              {link.title}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
              <span dir="ltr">{link.versionLabel}</span> · {link.fileName} · {formatBytes(link.fileSize)}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            {link.canDownload && (
              <Button variant="contained" disabled={busy} onClick={() => run(() => api.publicLink.download(token, session, link.fileName))}>
                {t.download}
              </Button>
            )}
            {link.canPrint && ready && (
              <Button
                variant="outlined"
                disabled={busy}
                onClick={() =>
                  run(async () => {
                    await api.publicLink.startPrint(token, session);
                    await printImages(link.pageCount, (number) => api.publicLink.page(token, session, number, 'print'));
                  })
                }
              >
                {busy ? t.preparingPrint : t.print}
              </Button>
            )}
          </Stack>
        </Stack>

        {error && <Alert severity="error">{error}</Alert>}
        {link.previewStatus === 'Pending' && <Alert severity="info">{t.previewPending}</Alert>}
        {link.previewStatus === 'NotSupported' && <Alert severity="info">{t.previewNotSupported}</Alert>}
        {link.previewStatus === 'Failed' && <Alert severity="warning">{t.previewFailed}</Alert>}

        {ready && (
          <>
            <Box sx={{ bgcolor: 'grey.100', borderRadius: 1, minHeight: 240, display: 'grid', placeItems: 'center', overflow: 'hidden' }}>
              {image.isLoading && <CircularProgress />}
              {image.data && (
                <Box
                  component="img"
                  src={image.data}
                  alt={`${t.page} ${page}`}
                  onContextMenu={(event) => event.preventDefault()}
                  sx={{ width: '100%', height: 'auto', display: 'block', userSelect: 'none' }}
                />
              )}
            </Box>
            {link.pageCount > 1 && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'center' }}>
                <Button size="small" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                  {t.previous}
                </Button>
                <Typography variant="body2">
                  {t.page} {formatNumber(page)} {t.of} {formatNumber(link.pageCount)}
                </Typography>
                <Button size="small" disabled={page >= link.pageCount} onClick={() => setPage(page + 1)}>
                  {t.next}
                </Button>
              </Stack>
            )}
          </>
        )}
        <Typography variant="caption" color="text.secondary">
          {s.linkWatermark}
        </Typography>
      </Stack>
    </Paper>
  );
}
