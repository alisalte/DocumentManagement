import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, type DocumentVersion, type Share, type ShareLink } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { describeError } from '../../strings';
import { Alert, Button, Card, Chip, ProgressBar } from '../ui';
import { ShareDialog } from './ShareDialog';
import { permissionLabels, s, stateLabels } from './sharingStrings';

const rowClasses = 'flex flex-col gap-2 rounded-lg px-2 py-3 transition-colors hover:bg-paper-50 sm:flex-row sm:items-center sm:gap-3';

/**
 * The shares and links of a document, with the button that makes new ones. Permission managers
 * see every share; everyone else sees the ones they made. Revoking takes effect at once.
 */
export function SharesPanel({
  documentId,
  versions,
  canDownload,
  canPrint,
  onChanged,
}: {
  documentId: string;
  versions: DocumentVersion[];
  canDownload: boolean;
  canPrint: boolean;
  onChanged: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const shares = useQuery({ queryKey: ['shares', documentId], queryFn: () => api.sharing.forDocument(documentId) });
  const [dialogOpen, setDialogOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const data = shares.data;
  if (!data || (!data.canShare && !data.canShareExternal && data.shares.length === 0 && data.links.length === 0)) {
    return null;
  }

  const refresh = async (message: string) => {
    setDialogOpen(false);
    onChanged(message);
    await queryClient.invalidateQueries({ queryKey: ['shares', documentId] });
  };

  const revoke = async (action: () => Promise<void>) => {
    if (!window.confirm(s.revokeConfirm)) return;
    setError(null);
    try {
      await action();
      await refresh(s.revoked);
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  // Only released files can be shared (decisions D6 and D9); the server refuses the rest anyway.
  const published = versions.filter(
    (version) => version.isPublished && !['Pending', 'Infected', 'Failed'].includes(version.scanStatus),
  );
  const rows = [
    ...data.shares.map((share) => ({ kind: 'share' as const, share, at: share.createdAt })),
    ...data.links.map((link) => ({ kind: 'link' as const, link, at: link.createdAt })),
  ].sort((left, right) => right.at.localeCompare(left.at));

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="grow text-base font-semibold text-ink-800">{s.shares}</h2>
        {(data.canShare || data.canShareExternal) && (
          <Button variant="outline" size="sm" onClick={() => setDialogOpen(true)}>
            {s.share}
          </Button>
        )}
      </div>

      {shares.isFetching && <ProgressBar className="mt-3" />}
      {error && (
        <Alert severity="error" className="mt-3">
          {error}
        </Alert>
      )}

      {rows.length === 0 ? (
        <p className="py-8 text-center text-sm text-paper-500">{s.noShares}</p>
      ) : (
        <ul className="mt-4 -mx-2 divide-y divide-paper-100">
          {rows.map((row) =>
            row.kind === 'share' ? (
              <ShareRow
                key={row.share.id}
                share={row.share}
                onRevoke={() => revoke(() => api.sharing.revoke(row.share.id))}
              />
            ) : (
              <LinkRow key={row.link.id} link={row.link} onRevoke={() => revoke(() => api.sharing.revokeLink(row.link.id))} />
            ),
          )}
        </ul>
      )}
      {!data.canManageAll && rows.length > 0 && <p className="mt-3 text-xs text-paper-500">{s.onlyYours}</p>}

      {dialogOpen && (
        <ShareDialog
          documentId={documentId}
          versions={published}
          canShare={data.canShare}
          canShareExternal={data.canShareExternal}
          canDownload={canDownload}
          canPrint={canPrint}
          onClose={() => setDialogOpen(false)}
          onShared={refresh}
        />
      )}
    </Card>
  );
}

function Permissions({ list }: { list: string[] }) {
  return <>{list.map((permission) => permissionLabels[permission as keyof typeof permissionLabels] ?? permission).join('، ')}</>;
}

function StateChip({ state }: { state: keyof typeof stateLabels }) {
  return <Chip size="small" color={state === 'Active' ? 'success' : 'default'} label={stateLabels[state]} />;
}

function ShareRow({ share, onRevoke }: { share: Share; onRevoke: () => void }) {
  return (
    <li className={rowClasses}>
      <div className="min-w-0 grow">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-ink-800">{share.sharedWith.displayName}</span>
          <Chip size="small" variant="outlined" label={<span dir="ltr">{share.versionLabel}</span>} />
          <StateChip state={share.state} />
        </div>
        <p className="mt-1 text-sm text-paper-500">
          <Permissions list={share.permissions} /> · {s.sharedBy} {share.sharedBy.displayName} · {formatDateTime(share.createdAt)}
          {share.expiresAt && (
            <>
              {' '}
              · {s.expires} {formatDateTime(share.expiresAt)}
            </>
          )}
        </p>
        {share.message && <p className="mt-1 text-sm text-ink-800 [overflow-wrap:anywhere]">{share.message}</p>}
      </div>
      {share.state === 'Active' && (
        <Button variant="danger" size="sm" className="self-start sm:self-center" onClick={onRevoke}>
          {s.revoke}
        </Button>
      )}
    </li>
  );
}

function LinkRow({ link, onRevoke }: { link: ShareLink; onRevoke: () => void }) {
  return (
    <li className={rowClasses}>
      <div className="min-w-0 grow">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-ink-800">
            {s.externalLink}{' '}
            <span dir="ltr" className="font-mono">
              {link.tokenPrefix}…
            </span>
          </span>
          <Chip size="small" variant="outlined" label={<span dir="ltr">{link.versionLabel}</span>} />
          <StateChip state={link.state} />
          {link.requiresPassword && <Chip size="small" variant="outlined" label={s.password} />}
        </div>
        {link.label && <p className="mt-1 text-sm text-ink-800">{link.label}</p>}
        <p className="mt-1 text-sm text-paper-500">
          <Permissions list={link.permissions} /> · {s.expires} {formatDateTime(link.expiresAt)} ·{' '}
          {formatNumber(link.accessCount)}
          {link.maxAccessCount !== null && <> / {formatNumber(link.maxAccessCount)}</>} {s.openings}
          {link.lockedUntil && (
            <>
              {' '}
              · {s.locked} {formatDateTime(link.lockedUntil)}
            </>
          )}
        </p>
        <p className="mt-1 text-xs text-paper-500">
          {link.createdBy.displayName} · {formatDateTime(link.createdAt)}
        </p>
      </div>
      {link.state === 'Active' && (
        <Button variant="danger" size="sm" className="self-start sm:self-center" onClick={onRevoke}>
          {s.revoke}
        </Button>
      )}
    </li>
  );
}
