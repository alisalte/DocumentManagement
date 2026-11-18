import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { permissionLabels, s } from '../components/sharing/sharingStrings';
import { Alert, Button, Card, Chip, EmptyState, PageHeader, ProgressBar, Toast } from '../components/ui';
import { FileTypeBadge } from '../components/FileTypeBadge';
import { ShareIcon } from '../components/icons';
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
    <div className="mx-auto max-w-[1000px] space-y-5">
      <PageHeader title={s.sharedWithMe} description="نسخه‌هایی از اسناد که همکاران با شما به اشتراک گذاشته‌اند." />
      {received.isFetching && <ProgressBar />}
      {received.isError && <Alert severity="error">{describeError(received.error)}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
      {received.data?.length === 0 && (
        <Card flush>
          <EmptyState icon={<ShareIcon />} title={s.sharedWithMeEmpty} />
        </Card>
      )}

      <div className="space-y-3">
        {received.data?.map((share) => (
          <Card key={share.id} className="transition-shadow hover:shadow-[var(--shadow-elevated)]">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <FileTypeBadge fileName={share.fileName} className="hidden sm:grid" />
              <div className="min-w-0 sm:flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <RouterLink
                    to={`/shared/${share.documentId}/${share.versionId}`}
                    className="font-semibold break-words text-ink-900 hover:text-ink-700"
                  >
                    {share.documentTitle}
                  </RouterLink>
                  <Chip variant="outlined" label={<span dir="ltr">{share.versionLabel}</span>} />
                </div>
                <p className="mt-1 text-sm break-words text-paper-600">
                  <bdi>{share.fileName}</bdi> · {formatBytes(share.fileSize)}
                </p>
                <p className="mt-0.5 text-sm text-paper-500">
                  {s.sharedBy} {share.sharedBy.displayName} · {formatDateTime(share.createdAt)} ·{' '}
                  {share.permissions.map((permission) => permissionLabels[permission]).join('، ')}
                  {share.expiresAt && <> · {s.expires} {formatDateTime(share.expiresAt)}</>}
                </p>
                {share.message && (
                  <p className="mt-2 rounded-lg border-s-2 border-ink-300 bg-paper-100/70 px-3 py-2 text-sm break-words text-paper-700">{share.message}</p>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Button size="sm" as={RouterLink} to={`/shared/${share.documentId}/${share.versionId}`}>
                  {s.open}
                </Button>
                <Button size="sm" variant="ghost" onClick={() => decline(share.id)}>
                  {s.decline}
                </Button>
              </div>
            </div>
          </Card>
        ))}
      </div>
      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} autoHideDuration={4000} />
    </div>
  );
}
