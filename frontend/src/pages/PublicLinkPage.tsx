import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useParams } from 'react-router';
import { printImages } from '../components/DocumentViewer';
import { s } from '../components/sharing/sharingStrings';
import {
  Alert,
  Button,
  Card,
  CenteredSpinner,
  Spinner,
  TextField,
} from '../components/ui';
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
    <div className="min-h-screen bg-slate-50 px-3 py-6 sm:px-6 sm:py-10">
      <div className="mx-auto w-full max-w-4xl space-y-4">
        <p className="text-sm font-semibold text-slate-500">{t.appTitle}</p>

        {!opened && info.isPending && <CenteredSpinner />}
        {!opened && info.isError && <Alert severity="error">{describeError(info.error)}</Alert>}

        {info.data && !opened && (
          <Card>
            <form onSubmit={open} className="space-y-4">
              <h1 className="text-xl font-bold text-slate-800">{s.linkTitle}</h1>
              {info.data.requiresPassword && (
                <>
                  <p className="text-sm text-slate-600">{s.linkNeedsPassword}</p>
                  <TextField
                    type="password"
                    label={t.password}
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    autoComplete="off"
                    required
                  />
                </>
              )}
              {info.data.lockedUntil && <Alert severity="warning">{s.linkLocked}</Alert>}
              {error && <Alert severity="error">{error}</Alert>}
              <p className="text-sm text-slate-500">{s.linkOpenHelp}</p>
              <Button
                type="submit"
                fullWidth
                loading={opening}
                disabled={opening || (info.data.requiresPassword && !password)}
              >
                {opening ? t.loading : s.linkOpen}
              </Button>
            </form>
          </Card>
        )}

        {opened && <OpenedLinkView token={token} link={opened} onEnded={ended} />}
      </div>
    </div>
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
    <Card>
      <div className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <h1 className="text-xl font-bold break-words text-slate-800">{link.title}</h1>
            <p className="mt-1 text-sm break-words text-slate-500">
              <span dir="ltr">{link.versionLabel}</span> · {link.fileName} · {formatBytes(link.fileSize)}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {link.canDownload && (
              <Button disabled={busy} onClick={() => run(() => api.publicLink.download(token, session, link.fileName))}>
                {t.download}
              </Button>
            )}
            {link.canPrint && ready && (
              <Button
                variant="outline"
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
          </div>
        </div>

        {error && <Alert severity="error">{error}</Alert>}
        {link.previewStatus === 'Pending' && <Alert severity="info">{t.previewPending}</Alert>}
        {link.previewStatus === 'NotSupported' && <Alert severity="info">{t.previewNotSupported}</Alert>}
        {link.previewStatus === 'Failed' && <Alert severity="warning">{t.previewFailed}</Alert>}

        {ready && (
          <>
            <div className="grid min-h-60 place-items-center overflow-hidden rounded-lg bg-slate-100">
              {image.isLoading && <Spinner />}
              {image.data && (
                <img
                  src={image.data}
                  alt={`${t.page} ${page}`}
                  onContextMenu={(event) => event.preventDefault()}
                  className="block h-auto w-full select-none"
                />
              )}
            </div>
            {link.pageCount > 1 && (
              <div className="flex flex-wrap items-center justify-center gap-2">
                <Button size="sm" variant="ghost" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                  {t.previous}
                </Button>
                <p className="text-sm text-slate-600">
                  {t.page} {formatNumber(page)} {t.of} {formatNumber(link.pageCount)}
                </p>
                <Button size="sm" variant="ghost" disabled={page >= link.pageCount} onClick={() => setPage(page + 1)}>
                  {t.next}
                </Button>
              </div>
            )}
          </>
        )}
        <p className="text-xs text-slate-500">{s.linkWatermark}</p>
      </div>
    </Card>
  );
}
