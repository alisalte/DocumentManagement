import { Alert, Button, Card, Chip, EmptyState, PageHeader, Pagination, ProgressBar, Select, Switch, cx } from '../components/ui';
import { FileTypeBadge } from '../components/FileTypeBadge';
import { SearchIcon } from '../components/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router';
import { approvalLabels } from '../components/workflow/workflowStrings';
import { api, type FacetBucket, type SearchHit } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatNumber } from '../lib/format';
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
  const inFile = params.get('file') === '1';
  const page = Number(params.get('page') ?? '1') || 1;

  const [text, setText] = useState(q);
  useEffect(() => setText(q), [q]);

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const types = useQuery({ queryKey: ['document-types'], queryFn: api.documentTypes });

  const search = useQuery({
    queryKey: ['search', q, categoryId, documentTypeId, tag, allVersions, inFile, page],
    queryFn: () => api.search({ q, categoryId, documentTypeId, tag, allVersions, inFile: inFile || undefined, page, pageSize }),
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

  const hasFacets =
    !!result &&
    !result.degraded &&
    ['category', 'documentType', 'tag'].some((key) => (result.facets[key]?.length ?? 0) > 0);
  const activeFilters = [categoryId, documentTypeId, tag, allVersions || null, inFile || null].filter(Boolean).length;

  return (
    <div className="space-y-5">
      <PageHeader
        title={t.searchEverything}
        description={<>{t.searchOcrHint} بخشی از کلمه هم کافی است و کنار هر نتیجه تعداد تکرار در متن فایل می‌آید.</>}
      />
      <form onSubmit={submit}>
        <Card className="space-y-4">
          <div className="flex flex-col gap-2 sm:flex-row">
            <div className="relative flex-1">
              <SearchIcon className="pointer-events-none absolute inset-y-0 start-3.5 my-auto size-5 text-paper-400" />
              <input
                type="search"
                aria-label={t.searchEverything}
                placeholder={t.searchPlaceholder}
                value={text}
                onChange={(event) => setText(event.target.value)}
                autoFocus
                enterKeyHint="search"
                className="h-11 w-full rounded-xl border border-paper-300 bg-paper-50 ps-11 pe-3 text-[15px] text-ink-900 shadow-[0_1px_2px_rgb(16_24_40/0.05)] transition-[border-color,box-shadow] placeholder:text-paper-400 hover:border-paper-400 focus:border-ink-500 focus:outline-none focus:ring-4 focus:ring-ink-500/15"
              />
            </div>
            <Button type="submit" size="lg" className="shrink-0">
              {t.searchButton}
            </Button>
          </div>

          <div className="grid gap-3 border-t border-paper-200 pt-4 sm:grid-cols-2 lg:grid-cols-[minmax(0,14rem)_minmax(0,14rem)_1fr] lg:items-end">
            <Select
              label={t.category}
              size="sm"
              value={categoryId ?? ''}
              onChange={(event) => update({ category: event.target.value || null })}
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
            >
              <option value="">{t.allTypes}</option>
              {(types.data ?? []).map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </Select>

            <div className="flex flex-wrap items-center gap-x-5 gap-y-2 sm:col-span-2 lg:col-span-1 lg:h-9">
              <Switch
                label={t.inFile}
                checked={inFile}
                onChange={(event) => update({ file: event.target.checked ? '1' : null })}
              />
              <Switch
                label={t.allVersions}
                checked={allVersions}
                onChange={(event) => update({ all: event.target.checked ? '1' : null })}
              />
            </div>
          </div>
          {inFile && <p className="text-xs text-paper-500">{t.inFileHelp}</p>}

          {(tag || activeFilters > 0) && (
            <div className="flex flex-wrap items-center gap-2">
              {tag && <Chip label={`${t.tags}: ${tag}`} onDelete={() => update({ tag: null })} color="primary" />}
              {activeFilters > 0 && (
                <button
                  type="button"
                  onClick={() => update({ category: null, type: null, tag: null, all: null, file: null })}
                  className="rounded-md px-1.5 py-0.5 text-xs font-medium text-paper-500 transition-colors hover:bg-paper-200/70 hover:text-ink-900"
                >
                  پاک کردن فیلترها
                </button>
              )}
            </div>
          )}
        </Card>
      </form>

      {search.isError && <Alert severity="error">{describeError(search.error)}</Alert>}
      {result?.degraded && <Alert severity="warning">{t.searchDegraded}</Alert>}

      {!result && search.isFetching && (
        <Card flush className="overflow-hidden">
          <ProgressBar />
          <p className="py-14 text-center text-sm text-paper-500">در حال جستجو…</p>
        </Card>
      )}

      {result && (
        <div className={cx('grid items-start gap-4', hasFacets && 'lg:grid-cols-[minmax(0,1fr)_17rem]')}>
          <Card flush className="min-w-0 overflow-hidden">
            <div className="flex items-center justify-between gap-3 border-b border-paper-200 px-4 py-3 sm:px-5">
              <p className="text-sm text-paper-500">
                <span className="font-semibold text-ink-900 tabular-nums">{formatNumber(result.total)}</span> {t.searchResults}
              </p>
            </div>
            {search.isFetching && <ProgressBar />}

            {result.hits.length === 0 ? (
              <EmptyState
                icon={<SearchIcon />}
                title={t.searchNothing}
                description="املای عبارت را بررسی کنید، کلمه‌ی کوتاه‌تری بنویسید یا فیلترها را بردارید."
              />
            ) : (
              <ul className={cx('divide-y divide-paper-200/70 transition-opacity', search.isPlaceholderData && 'opacity-60')}>
                {result.hits.map((hit) => (
                  <li key={`${hit.documentId}-${hit.versionId}`}>
                    <HitRow hit={hit} categoryName={hit.categoryId ? categoryName(hit.categoryId) : null} />
                  </li>
                ))}
              </ul>
            )}

            {result.total > 0 && (
              <div className="border-t border-paper-200 bg-paper-100/40 px-4 py-3 sm:px-5">
                <Pagination
                  count={pages}
                  page={Math.min(page, pages)}
                  total={result.total}
                  pageSize={pageSize}
                  disabled={search.isFetching}
                  onChange={(value) => {
                    update({ page: value > 1 ? String(value) : null });
                    document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' });
                  }}
                />
              </div>
            )}
          </Card>

          {hasFacets && (
            <div className="space-y-3 lg:sticky lg:top-6">
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
      className="group flex items-start gap-3 px-4 py-3.5 transition-colors hover:bg-paper-100/80 sm:px-5"
    >
      <FileTypeBadge fileName={hit.fileName} mimeType={hit.mimeType} />
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="break-words text-sm font-semibold text-ink-900 group-hover:text-ink-700">{hit.title}</span>
          {hit.label && <Chip label={hit.label} variant="outlined" dir="ltr" />}
          {!hit.isEffective && hit.isCurrent && <Chip size="small" label={t.draftVersion} />}
          {!hit.isEffective && !hit.isCurrent && <Chip size="small" variant="outlined" label={t.olderVersion} />}
          {hit.approvalStatus && hit.approvalStatus !== 'NotRequired' && hit.approvalStatus !== 'Approved' && (
            <Chip size="small" variant="outlined" label={approvalLabels[hit.approvalStatus] ?? hit.approvalStatus} />
          )}
        </div>

        {hit.matchCount != null && hit.matchCount > 0 && (
          <p className="text-xs font-medium text-ink-600">
            {formatNumber(hit.matchCount)} بار در متن این فایل
          </p>
        )}

        {hit.highlights.map((fragment, index) => (
          <p key={index} className="break-words text-sm leading-6 text-paper-600">
            {parseHighlight(fragment).map((segment, part) =>
              segment.marked ? (
                <mark key={part} className="rounded-sm bg-amber-100 px-0.5 font-medium text-inherit dark:bg-amber-500/25">
                  {segment.text}
                </mark>
              ) : (
                <span key={part}>{segment.text}</span>
              ),
            )}
            …
          </p>
        ))}

        <p className="text-xs text-paper-500">
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
    <Card className="p-4 sm:p-4">
      <h2 className="mb-2.5 text-xs font-semibold text-paper-500">{title}</h2>
      <div className="flex flex-wrap gap-1.5">
        {buckets.map((bucket) => (
          <button
            key={bucket.key}
            type="button"
            onClick={() => onPick(bucket.key)}
            className="inline-flex h-7 max-w-full items-center gap-1.5 rounded-lg border border-paper-200 bg-paper-50 px-2.5 text-xs font-medium text-paper-700 transition-colors hover:border-ink-200 hover:bg-ink-50 hover:text-ink-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink-500"
          >
            <span className="truncate">{label(bucket.key)}</span>
            <span className="text-paper-400 tabular-nums">{bucket.count.toLocaleString('fa-IR')}</span>
          </button>
        ))}
      </div>
    </Card>
  );
}
