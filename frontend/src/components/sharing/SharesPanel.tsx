import { Alert, Box, Button, Chip, Divider, LinearProgress, List, ListItem, Paper, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, type DocumentVersion, type Share, type ShareLink } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { describeError } from '../../strings';
import { ShareDialog } from './ShareDialog';
import { permissionLabels, s, stateLabels } from './sharingStrings';

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
    <Paper variant="outlined">
      <Stack direction="row" sx={{ alignItems: 'center', px: { xs: 2, sm: 3 }, pt: 2 }}>
        <Typography variant="h6" component="h2" sx={{ flexGrow: 1 }}>
          {s.shares}
        </Typography>
        {(data.canShare || data.canShareExternal) && (
          <Button variant="outlined" onClick={() => setDialogOpen(true)}>
            {s.share}
          </Button>
        )}
      </Stack>
      {shares.isFetching && <LinearProgress sx={{ mt: 1 }} />}
      {error && <Alert severity="error" sx={{ mx: 2, mt: 1 }}>{error}</Alert>}
      {rows.length === 0 ? (
        <Typography color="text.secondary" sx={{ px: { xs: 2, sm: 3 }, py: 2 }}>
          {s.noShares}
        </Typography>
      ) : (
        <List>
          {rows.map((row, index) => (
            <Box key={row.kind === 'share' ? row.share.id : row.link.id}>
              {index > 0 && <Divider component="li" />}
              {row.kind === 'share' ? (
                <ShareRow share={row.share} onRevoke={() => revoke(() => api.sharing.revoke(row.share.id))} />
              ) : (
                <LinkRow link={row.link} onRevoke={() => revoke(() => api.sharing.revokeLink(row.link.id))} />
              )}
            </Box>
          ))}
        </List>
      )}
      {!data.canManageAll && rows.length > 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ px: { xs: 2, sm: 3 }, pb: 2, display: 'block' }}>
          {s.onlyYours}
        </Typography>
      )}

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
    </Paper>
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
    <ListItem sx={{ px: { xs: 2, sm: 3 } }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ width: '100%', alignItems: { sm: 'center' } }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
            <Typography sx={{ fontWeight: 600 }}>{share.sharedWith.displayName}</Typography>
            <Chip size="small" variant="outlined" label={<span dir="ltr">{share.versionLabel}</span>} />
            <StateChip state={share.state} />
          </Stack>
          <Typography variant="body2" color="text.secondary">
            <Permissions list={share.permissions} /> · {s.sharedBy} {share.sharedBy.displayName} · {formatDateTime(share.createdAt)}
            {share.expiresAt && <> · {s.expires} {formatDateTime(share.expiresAt)}</>}
          </Typography>
          {share.message && (
            <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
              {share.message}
            </Typography>
          )}
        </Box>
        {share.state === 'Active' && (
          <Button size="small" color="error" onClick={onRevoke}>
            {s.revoke}
          </Button>
        )}
      </Stack>
    </ListItem>
  );
}

function LinkRow({ link, onRevoke }: { link: ShareLink; onRevoke: () => void }) {
  return (
    <ListItem sx={{ px: { xs: 2, sm: 3 } }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ width: '100%', alignItems: { sm: 'center' } }}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
            <Typography sx={{ fontWeight: 600 }}>
              {s.externalLink}{' '}
              <Box component="span" dir="ltr" sx={{ fontFamily: 'monospace' }}>
                {link.tokenPrefix}…
              </Box>
            </Typography>
            <Chip size="small" variant="outlined" label={<span dir="ltr">{link.versionLabel}</span>} />
            <StateChip state={link.state} />
            {link.requiresPassword && <Chip size="small" variant="outlined" label={s.password} />}
          </Stack>
          {link.label && <Typography variant="body2">{link.label}</Typography>}
          <Typography variant="body2" color="text.secondary">
            <Permissions list={link.permissions} /> · {s.expires} {formatDateTime(link.expiresAt)} ·{' '}
            {formatNumber(link.accessCount)}
            {link.maxAccessCount !== null && <> / {formatNumber(link.maxAccessCount)}</>} {s.openings}
            {link.lockedUntil && <> · {s.locked} {formatDateTime(link.lockedUntil)}</>}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {link.createdBy.displayName} · {formatDateTime(link.createdAt)}
          </Typography>
        </Box>
        {link.state === 'Active' && (
          <Button size="small" color="error" onClick={onRevoke}>
            {s.revoke}
          </Button>
        )}
      </Stack>
    </ListItem>
  );
}
