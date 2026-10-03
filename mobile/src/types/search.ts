export interface SearchHit {
  documentId: string;
  versionId: string;
  label: string;
  title: string;
  fileName: string | null;
  mimeType: string | null;
  categoryId: string | null;
  documentTypeId: string | null;
  isCurrent: boolean;
  isEffective: boolean;
  approvalStatus: string | null;
  updatedAt: string | null;
  highlights: string[];
  matchCount: number | null;
}

export interface SearchResult {
  hits: SearchHit[];
  total: number;
  page: number;
  pageSize: number;
  facets: Record<string, { key: string; count: number }[]>;
  /** The search engine is unavailable; only titles were searched. */
  degraded: boolean;
}

export interface SearchParams {
  q?: string;
  categoryId?: string | null;
  documentTypeId?: string | null;
  tag?: string | null;
  allVersions?: boolean;
  inFile?: boolean;
  page?: number;
  pageSize?: number;
}
