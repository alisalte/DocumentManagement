import { Alert, Box, Button, Chip, LinearProgress, Paper, Snackbar, Stack, Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { permissionLabels, s } from '../components/sharing/sharingStrings';
import { api } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { describeError } from '../strings';

/**
 * Versions colleagues shared with the signed-in user. Only shares that work right now are
 * listed: one whose sharer lost the right, or that a DENY overrides, simply is not here.
 */
export function SharedWithMePage() {
  const queryClient = useQueryClient();
  const received = useQuery({ queryKey: ['shares-received'], queryFn: api.sharing.received });
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const decline = async (shareId: string) => {
    if (!window.confirm(s.revokeConfirm)) return;
    setError(null);
    try {
      await api.sharing.revoke(shareId);
      setNotice(s.revoked);
      await queryClient.invalidateQueries({ queryKey: ['shares-received'] });
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Typography variant="h5" component="h1">
        {s.sharedWithMe}
      </Typography>
      {received.isFetching && <LinearProgress />}
      {received.isError && <Alert severity="error">{describeError(received.error)}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
      {received.data?.length === 0 && <Typography color="text.secondary">{s.sharedWithMeEmpty}</Typography>}

      {received.data?.map((share) => (
        <Paper key={share.id} variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { sm: 'center' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
                <Typography
                  component={RouterLink}
                  to={`/shared/${share.documentId}/${share.versionId}`}
                  variant="subtitle1"
                  sx={{ fontWeight: 600, color: 'inherit', overflowWrap: 'anywhere' }}
                >
                  {share.documentTitle}
                </Typography>
                <Chip size="small" variant="outlined" label={<span dir="ltr">{share.versionLabel}</span>} />
              </Stack>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                {share.fileName} · {formatBytes(share.fileSize)}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {s.sharedBy} {share.sharedBy.displayName} · {formatDateTime(share.createdAt)} ·{' '}
                {share.permissions.map((permission) => permissionLabels[permission]).join('، ')}
                {share.expiresAt && <> · {s.expires} {formatDateTime(share.expiresAt)}</>}
              </Typography>
              {share.message && (
                <Typography variant="body2" sx={{ mt: 0.5, overflowWrap: 'anywhere' }}>
                  {share.message}
                </Typography>
              )}
            </Box>
            <Stack direction="row" spacing={0.5}>
              <Button variant="contained" size="small" component={RouterLink} to={`/shared/${share.documentId}/${share.versionId}`}>
                {s.open}
              </Button>
              <Button size="small" onClick={() => decline(share.id)}>
                {s.decline}
              </Button>
            </Stack>
          </Stack>
        </Paper>
      ))}
      <Snackbar open={!!notice} autoHideDuration={4000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
  );
}
