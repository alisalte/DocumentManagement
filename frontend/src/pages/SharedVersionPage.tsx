import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink, useParams } from 'react-router';
import { DocumentViewer } from '../components/DocumentViewer';
import { MetadataView } from '../components/metadata/MetadataView';
import { s } from '../components/sharing/sharingStrings';
import { Alert, Button, Card, CenteredSpinner, Chip } from '../components/ui';
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
    return (
      <div className="space-y-4 sm:space-y-5">
        <CenteredSpinner />
      </div>
    );
  }

  if (details.isError) {
    return (
      <div className="space-y-4 sm:space-y-5">
        <Alert
          severity="error"
          action={
            <Button as={RouterLink} to="/shared" variant="outline" size="sm">
              {t.back}
            </Button>
          }
        >
          {describeError(details.error)}
        </Alert>
      </div>
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
    <div className="max-w-[1000px] space-y-4 sm:space-y-5">
      <Card>
        <div className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <h1 className="text-xl font-bold break-words text-slate-800">{title}</h1>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <Chip label={s.sharedWithMe} />
                <span className="text-sm text-slate-500" dir="ltr">
                  {version.label}
                </span>
              </div>
            </div>
            {allowedActions.includes('DOCUMENT_DOWNLOAD') && <Button onClick={download}>{t.download}</Button>}
          </div>

          {description && <p className="text-sm break-words whitespace-pre-wrap text-slate-700">{description}</p>}
          <p className="text-sm break-words text-slate-500">
            {version.fileName} · {formatBytes(version.fileSize)} · {formatDateTime(version.createdAt)}
          </p>
          {schema.data && schema.data.fields.length > 0 && (
            <div>
              <h2 className="mb-2 text-base font-semibold text-slate-800">{t.metadata}</h2>
              <MetadataView schema={schema.data} metadata={version.metadata} />
            </div>
          )}
          <p className="text-xs text-slate-500">{s.versionHelp}</p>
          {error && <Alert severity="error">{error}</Alert>}
        </div>
      </Card>

      <DocumentViewer documentId={documentId} versionId={versionId} canReprocess={false} />
    </div>
  );
}
