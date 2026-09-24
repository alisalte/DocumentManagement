import { Alert, Box, Button, Chip, LinearProgress, Paper, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink, useParams } from 'react-router';
import { DocumentViewer } from '../components/DocumentViewer';
import { MetadataView } from '../components/metadata/MetadataView';
import { s } from '../components/sharing/sharingStrings';
import { api } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { describeError, t } from '../strings';

/**
 * One version, as a share recipient sees it: the title, that version's metadata and file, the
 * viewer, and download or print only when the share includes them. No history, no other
 * versions: the share is pinned to this one (decision D8).
 */
export function SharedVersionPage() {
  const { documentId = '', versionId = '' } = useParams();
  const [error, setError] = useState<string | null>(null);
  const details = useQuery({
    queryKey: ['shared-version', documentId, versionId],
    queryFn: () => api.version(documentId, versionId),
  });

  const schemaId = details.data?.version.schemaVersionId;
  const schema = useQuery({
    queryKey: ['schema', schemaId],
    queryFn: () => api.schema(schemaId!),
    enabled: !!schemaId,
    staleTime: Infinity,
  });

  if (details.isPending) {
    return <LinearProgress />;
  }

  if (details.isError) {
    return (
      <Alert severity="error" action={<Button component={RouterLink} to="/shared">{t.back}</Button>}>
        {describeError(details.error)}
      </Alert>
    );
  }

  const { title, description, version, allowedActions } = details.data;
  const download = async () => {
    setError(null);
    try {
      await api.download(documentId, versionId, version.fileName);
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Paper variant="outlined" sx={{ p: { xs: 2, sm: 3 } }}>
        <Stack spacing={1.5}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'flex-start' } }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="h5" component="h1" sx={{ overflowWrap: 'anywhere' }}>
                {title}
              </Typography>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
                <Chip size="small" label={s.sharedWithMe} />
                <Typography variant="body2" color="text.secondary" dir="ltr">
                  {version.label}
                </Typography>
              </Stack>
            </Box>
            {allowedActions.includes('DOCUMENT_DOWNLOAD') && (
              <Button variant="contained" onClick={download}>
                {t.download}
              </Button>
            )}
          </Stack>
          {description && <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{description}</Typography>}
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            {version.fileName} · {formatBytes(version.fileSize)} · {formatDateTime(version.createdAt)}
          </Typography>
          {schema.data && schema.data.fields.length > 0 && (
            <Box>
              <Typography variant="subtitle1" component="h2" sx={{ mb: 1 }}>
                {t.metadata}
              </Typography>
              <MetadataView schema={schema.data} metadata={version.metadata} />
            </Box>
          )}
          <Typography variant="caption" color="text.secondary">
            {s.versionHelp}
          </Typography>
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </Paper>

      <DocumentViewer documentId={documentId} versionId={versionId} canReprocess={false} />
    </Stack>
  );
}
