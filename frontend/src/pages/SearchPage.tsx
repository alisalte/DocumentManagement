import { Alert, Button, Card, Chip, ProgressBar, Select, Switch, TextField } from '../components/ui';
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
    <div className="max-w-[1000px] space-y-5">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{t.searchEverything}</h1>
        <p className="mt-1 text-sm text-paper-500">جستجو در عنوان، محتوا و فرادادهٔ اسناد قابل‌مشاهده.</p>
      </div>
      <form onSubmit={submit}>
        <Card className="space-y-4">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
            <TextField
              label={t.searchEverything}
              placeholder={t.searchPlaceholder}
              value={text}
              onChange={(event) => setText(event.target.value)}
              autoFocus
              enterKeyHint="search"
              size="sm"
              className="flex-1"
            />
            <Button type="submit" className="shrink-0">
              {t.searchButton}
            </Button>
          </div>

          <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end">
            <Select
              label={t.category}
              size="sm"
              value={categoryId ?? ''}
              onChange={(event) => update({ category: event.target.value || null })}
              className="sm:w-auto sm:min-w-[180px]"
            >
              <option value="">{t.allCategories}</option>
              {(categories.data ?? [])
                .filter((category) => category.canView)
                .map((category) => (
                  <option
                    key={category.id}
                    value={category.id}
                    style={{ paddingInlineStart: `${16 + category.depth * 16}px` }}
                  >
                    {category.name}
                  </option>
                ))}
            </Select>

            <Select
              label={t.documentType}
              size="sm"
              value={documentTypeId ?? ''}
              onChange={(event) => update({ type: event.target.value || null })}
              className="sm:w-auto sm:min-w-[180px]"
            >
              <option value="">{t.allTypes}</option>
              {(types.data ?? []).map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </Select>

            <div className="shrink-0">
              <Switch
                label={t.allVersions}
                checked={allVersions}
                onChange={(event) => update({ all: event.target.checked ? '1' : null })}
              />
            </div>
          </div>

          {tag && (
            <div>
              <Chip label={`${t.tags}: ${tag}`} onDelete={() => update({ tag: null })} size="small" />
            </div>
          )}
        </Card>
      </form>

      {search.isFetching && <ProgressBar />}
      {search.isError && <Alert severity="error">{describeError(search.error)}</Alert>}
      {result?.degraded && <Alert severity="warning">{t.searchDegraded}</Alert>}

      {result && (
        <div className="flex flex-col items-start gap-4 md:flex-row">
          <Card className="w-full min-w-0 flex-1">
            <p className="text-sm text-paper-500">
              {result.total.toLocaleString('fa-IR')} {t.searchResults}
            </p>

            {result.hits.length === 0 ? (
              <p className="py-10 text-center text-sm text-paper-500">{t.searchNothing}</p>
            ) : (
              <ul className="-mx-2 mt-3 divide-y divide-paper-100 sm:-mx-3">
                {result.hits.map((hit) => (
                  <li key={`${hit.documentId}-${hit.versionId}`}>
                    <HitRow hit={hit} categoryName={hit.categoryId ? categoryName(hit.categoryId) : null} />
                  </li>
                ))}
              </ul>
            )}

            {pages > 1 && (
              <div className="mt-4 flex flex-wrap items-center justify-center gap-3">
                <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
                  {t.previous}
                </Button>
                <span className="text-sm text-paper-500">
                  {page.toLocaleString('fa-IR')} {t.of} {pages.toLocaleString('fa-IR')}
                </span>
                <Button size="sm" variant="outline" disabled={page >= pages} onClick={() => update({ page: String(page + 1) })}>
                  {t.next}
                </Button>
              </div>
            )}
          </Card>

          {!result.degraded && (
            <div className="w-full shrink-0 space-y-3 md:w-64">
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
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function HitRow({ hit, categoryName }: { hit: SearchHit; categoryName: string | null }) {
  return (
    <RouterLink
      to={`/documents/${hit.documentId}`}
      className="block rounded-lg px-2 py-3 transition-colors hover:bg-paper-50 sm:px-3"
    >
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="break-words font-semibold text-ink-800">{hit.title}</span>
          {hit.label && (
            <span dir="ltr" className="text-sm text-paper-500">
              {hit.label}
            </span>
          )}
          {!hit.isEffective && hit.isCurrent && <Chip size="small" label={t.draftVersion} />}
          {!hit.isEffective && !hit.isCurrent && <Chip size="small" variant="outlined" label={t.olderVersion} />}
          {hit.approvalStatus && hit.approvalStatus !== 'NotRequired' && hit.approvalStatus !== 'Approved' && (
            <Chip size="small" variant="outlined" label={approvalLabels[hit.approvalStatus] ?? hit.approvalStatus} />
          )}
        </div>

        {hit.highlights.map((fragment, index) => (
          <p key={index} className="break-words text-sm text-paper-500">
            {parseHighlight(fragment).map((segment, part) =>
              segment.marked ? (
                <mark key={part} className="rounded-sm bg-amber-100 px-0.5 text-inherit">
                  {segment.text}
                </mark>
              ) : (
                <span key={part}>{segment.text}</span>
              ),
            )}
            …
          </p>
        ))}

        <p className="text-xs text-paper-400">
          {[categoryName, hit.fileName, hit.updatedAt ? formatDateTime(hit.updatedAt) : null].filter(Boolean).join(' · ')}
        </p>
      </div>
    </RouterLink>
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
    <Card>
      <h2 className="mb-2 text-sm font-semibold text-ink-800">{title}</h2>
      <div className="flex flex-wrap gap-1.5">
        {buckets.map((bucket) => (
          <button
            key={bucket.key}
            type="button"
            onClick={() => onPick(bucket.key)}
            className="inline-flex h-6 max-w-full items-center rounded-full bg-paper-100 px-2.5 text-xs font-medium text-ink-800 transition-colors hover:bg-ink-100 hover:text-ink-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-600"
          >
            <span className="truncate">
              {label(bucket.key)} ({bucket.count.toLocaleString('fa-IR')})
            </span>
          </button>
        ))}
      </div>
    </Card>
  );
}
