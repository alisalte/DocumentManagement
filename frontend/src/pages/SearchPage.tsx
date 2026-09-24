import {
  Alert,
  Box,
  Button,
  Chip,
  Divider,
  FormControlLabel,
  LinearProgress,
  List,
  ListItemButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router';
import { approvalLabels } from '../components/workflow/workflowStrings';
import { api, type FacetBucket, type SearchHit } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { parseHighlight } from '../lib/highlight';
import { describeError, t } from '../strings';

const pageSize = 20;

/**
 * Full-text search (section 8.3). Everything lives in the address bar, so a search can be
 * bookmarked or shared; the server limits the results, totals and facet counts to what the
 * caller may see.
 */
export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const categoryId = params.get('category');
  const documentTypeId = params.get('type');
  const tag = params.get('tag');
  const allVersions = params.get('all') === '1';
  const page = Number(params.get('page') ?? '1') || 1;

  const [text, setText] = useState(q);
  useEffect(() => setText(q), [q]);

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const types = useQuery({ queryKey: ['document-types'], queryFn: api.documentTypes });

  const search = useQuery({
    queryKey: ['search', q, categoryId, documentTypeId, tag, allVersions, page],
    queryFn: () => api.search({ q, categoryId, documentTypeId, tag, allVersions, page, pageSize }),
    placeholderData: keepPreviousData,
  });

  const update = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }

    if (!('page' in changes)) next.delete('page');
    setParams(next);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    update({ q: text.trim() || null });
  };

  const categoryName = (id: string) => categories.data?.find((category) => category.id === id)?.name ?? '…';
  const typeName = (id: string) => types.data?.find((type) => type.id === id)?.name ?? '…';
  const result = search.data;
  const pages = result ? Math.max(1, Math.ceil(result.total / pageSize)) : 1;

  return (
    <Stack spacing={2} sx={{ maxWidth: 1000 }}>
      <Paper variant="outlined" component="form" onSubmit={submit} sx={{ p: { xs: 1.5, sm: 2 } }}>
        <Stack spacing={1.5}>
          <Stack direction="row" spacing={1}>
            <TextField
              fullWidth
              size="small"
              label={t.searchEverything}
              placeholder={t.searchPlaceholder}
              value={text}
              onChange={(event) => setText(event.target.value)}
              autoFocus
              slotProps={{ htmlInput: { enterKeyHint: 'search' } }}
            />
            <Button type="submit" variant="contained">
              {t.searchButton}
            </Button>
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
            <TextField
              select
              size="small"
              label={t.category}
              value={categoryId ?? ''}
              onChange={(event) => update({ category: event.target.value || null })}
              sx={{ minWidth: 180 }}
            >
              <MenuItem value="">{t.allCategories}</MenuItem>
              {(categories.data ?? [])
                .filter((category) => category.canView)
                .map((category) => (
                  <MenuItem key={category.id} value={category.id} sx={{ pl: 2 + category.depth * 2 }}>
                    {category.name}
                  </MenuItem>
                ))}
            </TextField>
            <TextField
              select
              size="small"
              label={t.documentType}
              value={documentTypeId ?? ''}
              onChange={(event) => update({ type: event.target.value || null })}
              sx={{ minWidth: 180 }}
            >
              <MenuItem value="">{t.allTypes}</MenuItem>
              {(types.data ?? []).map((type) => (
                <MenuItem key={type.id} value={type.id}>
                  {type.name}
                </MenuItem>
              ))}
            </TextField>
            <FormControlLabel
              control={<Switch checked={allVersions} onChange={(_, checked) => update({ all: checked ? '1' : null })} />}
              label={t.allVersions}
            />
          </Stack>
          {tag && (
            <Box>
              <Chip label={`${t.tags}: ${tag}`} onDelete={() => update({ tag: null })} size="small" />
            </Box>
          )}
        </Stack>
      </Paper>

      {search.isFetching && <LinearProgress />}
      {search.isError && <Alert severity="error">{describeError(search.error)}</Alert>}
      {result?.degraded && <Alert severity="warning">{t.searchDegraded}</Alert>}

      {result && (
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: 'flex-start' }}>
          <Paper variant="outlined" sx={{ flexGrow: 1, minWidth: 0, width: '100%' }}>
            <Typography variant="body2" color="text.secondary" sx={{ px: 2, pt: 1.5 }}>
              {result.total.toLocaleString('fa-IR')} {t.searchResults}
            </Typography>
            {result.hits.length === 0 ? (
              <Typography sx={{ p: 2 }} color="text.secondary">
                {t.searchNothing}
              </Typography>
            ) : (
              <List>
                {result.hits.map((hit, index) => (
                  <Box key={`${hit.documentId}-${hit.versionId}`}>
                    {index > 0 && <Divider component="li" />}
                    <HitRow hit={hit} categoryName={hit.categoryId ? categoryName(hit.categoryId) : null} />
                  </Box>
                ))}
              </List>
            )}
            {pages > 1 && (
              <Stack direction="row" spacing={1} sx={{ justifyContent: 'center', pb: 1.5 }}>
                <Button size="small" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
                  {t.previous}
                </Button>
                <Typography variant="body2" sx={{ alignSelf: 'center' }}>
                  {page.toLocaleString('fa-IR')} {t.of} {pages.toLocaleString('fa-IR')}
                </Typography>
                <Button size="small" disabled={page >= pages} onClick={() => update({ page: String(page + 1) })}>
                  {t.next}
                </Button>
              </Stack>
            )}
          </Paper>

          {!result.degraded && (
            <Stack spacing={1.5} sx={{ width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
              <Facet
                title={t.category}
                buckets={result.facets.category}
                label={categoryName}
                onPick={(key) => update({ category: key })}
              />
              <Facet
                title={t.documentType}
                buckets={result.facets.documentType}
                label={typeName}
                onPick={(key) => update({ type: key })}
              />
              <Facet title={t.tags} buckets={result.facets.tag} label={(key) => key} onPick={(key) => update({ tag: key })} />
            </Stack>
          )}
        </Stack>
      )}
    </Stack>
  );
}

function HitRow({ hit, categoryName }: { hit: SearchHit; categoryName: string | null }) {
  return (
    <ListItemButton component={RouterLink} to={`/documents/${hit.documentId}`} sx={{ px: 2, alignItems: 'flex-start' }}>
      <Stack spacing={0.5} sx={{ minWidth: 0, width: '100%' }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
          <Typography sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>{hit.title}</Typography>
          {hit.label && (
            <Typography variant="body2" color="text.secondary" dir="ltr">
              {hit.label}
            </Typography>
          )}
          {!hit.isEffective && hit.isCurrent && <Chip size="small" label={t.draftVersion} />}
          {!hit.isEffective && !hit.isCurrent && <Chip size="small" variant="outlined" label={t.olderVersion} />}
          {hit.approvalStatus && hit.approvalStatus !== 'NotRequired' && hit.approvalStatus !== 'Approved' && (
            <Chip size="small" variant="outlined" label={approvalLabels[hit.approvalStatus] ?? hit.approvalStatus} />
          )}
        </Stack>
        {hit.highlights.map((fragment, index) => (
          <Typography key={index} variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
            {parseHighlight(fragment).map((segment, part) =>
              segment.marked ? (
                <Box key={part} component="mark" sx={{ bgcolor: 'warning.light', color: 'inherit', px: 0.25, borderRadius: 0.5 }}>
                  {segment.text}
                </Box>
              ) : (
                <span key={part}>{segment.text}</span>
              ),
            )}
            …
          </Typography>
        ))}
        <Typography variant="caption" color="text.secondary">
          {[categoryName, hit.fileName, hit.updatedAt ? formatDateTime(hit.updatedAt) : null].filter(Boolean).join(' · ')}
        </Typography>
      </Stack>
    </ListItemButton>
  );
}

function Facet({
  title,
  buckets,
  label,
  onPick,
}: {
  title: string;
  buckets: FacetBucket[] | undefined;
  label: (key: string) => string;
  onPick: (key: string) => void;
}) {
  if (!buckets || buckets.length === 0) {
    return null;
  }

  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        {title}
      </Typography>
      <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
        {buckets.map((bucket) => (
          <Chip
            key={bucket.key}
            size="small"
            clickable
            onClick={() => onPick(bucket.key)}
            label={`${label(bucket.key)} (${bucket.count.toLocaleString('fa-IR')})`}
          />
        ))}
      </Stack>
    </Paper>
  );
}
