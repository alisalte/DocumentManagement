/**
 * Thin API client.
 *
 * The access token is short lived and kept in memory only; the refresh token lives in
 * sessionStorage for the dev shell. Permissions returned by the API are used to hide UI that
 * would fail anyway, never to decide access: the server is the only authority.
 */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080';
const refreshKey = 'dms.refreshToken';

let accessToken: string | null = null;
let refreshing: Promise<boolean> | null = null;

export interface CurrentUser {
  id: string;
  username: string;
  displayName: string;
  email: string | null;
  isSystemAdmin: boolean;
  mustChangePassword: boolean;
  groupIds: string[];
  systemPermissions: string[];
}

export interface AuthTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: {
    id: string;
    username: string;
    displayName: string;
    isSystemAdmin: boolean;
    mustChangePassword: boolean;
  };
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  code: string;
  description: string | null;
  depth: number;
  isActive: boolean;
  sortOrder: number;
  canView: boolean;
  canCreate: boolean;
}

export interface DocumentListItem {
  id: string;
  title: string;
  categoryId: string;
  documentTypeId: string;
  ownerId: string;
  currentVersionLabel: string | null;
  fileName: string | null;
  mimeType: string | null;
  fileSize: number | null;
  updatedAt: string;
  deletedAt: string | null;
  deleteReason: string | null;
}

export interface Tag {
  id: string;
  name: string;
}

export interface DocumentDetails {
  id: string;
  title: string;
  description: string | null;
  categoryId: string;
  categoryName: string;
  documentTypeId: string;
  ownerId: string;
  currentVersionId: string | null;
  effectiveVersionId: string | null;
  currentMetadata: Metadata | null;
  currentSchemaVersionId: string | null;
  latestVersionNumber: number;
  status: string;
  tags: Tag[];
  createdBy: string;
  createdAt: string;
  updatedBy: string;
  updatedAt: string;
  deletedAt: string | null;
  deleteReason: string | null;
  allowedActions: string[];
}

export interface DocumentVersion {
  id: string;
  versionNumber: number;
  revisionNumber: number;
  label: string;
  storageObjectId: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  sha256: string;
  changeKind: string;
  changeDescription: string | null;
  approvalStatus: string;
  isPublished: boolean;
  createdBy: string;
  createdAt: string;
  isCurrent: boolean;
  isEffective: boolean;
  scanStatus: string;
  schemaVersionId: string;
  metadata: Metadata | null;
}

export interface DocumentType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  latestPublishedVersionId: string | null;
  settings?: DocumentTypeSettings;
}

export type FieldType =
  | 'Text' | 'LongText' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime' | 'Select' | 'MultiSelect'
  | 'User' | 'Group' | 'DocumentReference' | 'Url' | 'Email' | 'Phone';

export interface LocalizedText {
  fa: string;
  en?: string | null;
}

export interface FieldValidation {
  minLength?: number | null;
  maxLength?: number | null;
  min?: number | null;
  max?: number | null;
  pattern?: string | null;
  patternMessage?: LocalizedText | null;
  minDate?: string | null;
  maxDate?: string | null;
  scale?: number | null;
  maxItems?: number | null;
}

export interface FieldOption {
  value: string;
  label: LocalizedText;
  displayOrder: number;
  isActive: boolean;
}

export interface FieldSchema {
  code: string;
  label: LocalizedText;
  type: FieldType;
  isRequired: boolean;
  isSearchable: boolean;
  isSortable: boolean;
  showInList: boolean;
  isApprovalRelevant: boolean;
  defaultValue?: unknown;
  validation: FieldValidation;
  options: FieldOption[];
  helpText?: LocalizedText | null;
  displayOrder: number;
  isActive: boolean;
}

export type RuleKind = 'Show' | 'Require' | 'Validate';

export interface FieldRuleSchema {
  kind: RuleKind;
  condition?: unknown;
  targets: string[];
  assertion?: unknown;
  message?: LocalizedText | null;
  displayOrder: number;
}

export interface DocumentTypeSchema {
  documentTypeId: string;
  versionId: string;
  versionNumber: number;
  isPublished: boolean;
  fields: FieldSchema[];
  rules: FieldRuleSchema[];
}

export type MetadataEditPolicy = 'NewRevision' | 'InPlace';

export type WorkflowMode = 'None' | 'Manual' | 'AutoOnVersion';

export interface DocumentTypeSettings {
  workflowId: string | null;
  workflowMode: WorkflowMode;
  allowedExtensions: string[];
  maxUploadBytes: number | null;
  metadataEditPolicy: MetadataEditPolicy;
  allowExternalSharing: boolean;
}

export interface DocumentTypeAdmin {
  type: DocumentType & { settings: DocumentTypeSettings };
  versions: { id: string; versionNumber: number; status: string; publishedAt: string | null; fieldCount: number }[];
  draft: DocumentTypeSchema | null;
}

export type Metadata = Record<string, unknown>;

export interface MetadataUpdate {
  documentId: string;
  versionId: string;
  label: string;
  outcome: 'revision' | 'in_place' | 'unchanged';
}

export type WorkflowAction = 'Approve' | 'Reject' | 'Return' | 'RequestChanges' | 'Forward';
export type AssigneeType = 'User' | 'Group' | 'Role' | 'DocumentOwner' | 'Creator' | 'Manager' | 'DynamicUserField';

export interface WorkflowTask {
  id: string;
  instanceId: string;
  documentId: string;
  documentTitle: string;
  versionId: string;
  versionLabel: string;
  stepCode: string;
  stepName: string;
  status: 'Pending' | 'Completed' | 'Cancelled';
  assignedUserId: string | null;
  assignedGroupId: string | null;
  assignedRoleId: string | null;
  createdAt: string;
  dueAt: string | null;
  completedAt: string | null;
  completedBy: string | null;
  action: WorkflowAction | null;
  comment: string | null;
  forwardedFromTaskId: string | null;
  allowedActions: WorkflowAction[];
  canAct: boolean;
  isOverdue: boolean;
}

export interface WorkflowInstance {
  id: string;
  versionId: string;
  versionLabel: string;
  workflowVersionId: string;
  status: 'Running' | 'Approved' | 'Rejected' | 'ChangesRequested' | 'Cancelled';
  currentSequence: number;
  startedBy: string;
  startedAt: string;
  completedAt: string | null;
  cancelReason: string | null;
  attentionReason: string | null;
  skippedSteps: string[];
  tasks: WorkflowTask[];
  canCancel: boolean;
}

export interface StepAction {
  action: WorkflowAction;
  commentRequired: boolean;
  targetStepCode: string | null;
}

export interface WorkflowStep {
  code: string;
  name: string;
  sequence: number;
  assigneeType: AssigneeType;
  assigneeId: string | null;
  assigneeFieldCode: string | null;
  completionRule: 'Any' | 'All';
  isRequired: boolean;
  slaHours: number | null;
  allowSelfApproval: boolean;
  condition?: unknown;
  actions: StepAction[];
}

export interface WorkflowDefinition {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  latestPublishedVersionId: string | null;
}

export interface WorkflowAdmin {
  workflow: WorkflowDefinition;
  versions: { id: string; versionNumber: number; status: string; publishedAt: string | null; stepCount: number }[];
  draft: WorkflowStep[] | null;
}

export interface RoleSummary {
  id: string;
  code: string;
  name: string;
}

export interface DirectoryEntry {
  id: string;
  displayName?: string;
  name?: string;
  username?: string;
  code?: string;
}

export interface UploadResult {
  uploadId: string;
  fileName: string;
  mimeType: string;
  size: number;
  sha256: string;
  duplicates: { documentId: string; title: string }[];
}

export interface CreatedVersion {
  documentId: string;
  versionId: string;
  label: string;
}

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
  /** Engine fragments: HTML-escaped text with <mark> around the matches. */
  highlights: string[];
}

export interface FacetBucket {
  key: string;
  count: number;
}

export interface SearchResult {
  hits: SearchHit[];
  total: number;
  page: number;
  pageSize: number;
  facets: Record<string, FacetBucket[]>;
  /** The search engine is unavailable; only titles were searched. */
  degraded: boolean;
}

export interface SearchParams {
  q?: string;
  categoryId?: string | null;
  documentTypeId?: string | null;
  mimeType?: string | null;
  tag?: string | null;
  allVersions?: boolean;
  page?: number;
  pageSize?: number;
}

export type RenditionStatus = 'Pending' | 'Ready' | 'Failed' | 'NotSupported';

export interface PreviewInfo {
  versionId: string;
  label: string;
  status: RenditionStatus;
  pageCount: number;
  error: string | null;
  canPrint: boolean;
  canDownload: boolean;
}

export interface SearchStatus {
  engineEnabled: boolean;
  extractorEnabled: boolean;
  index: string | null;
  /** -1 when the engine did not answer. */
  indexedVersions: number;
  extractions: Record<string, number>;
}

export type SharePermission = 'View' | 'Download' | 'Print';
export type ShareState = 'Active' | 'Expired' | 'Revoked' | 'UsedUp';

export interface Person {
  id: string;
  displayName: string;
}

export interface Share {
  id: string;
  versionId: string;
  versionLabel: string;
  sharedWith: Person;
  sharedBy: Person;
  permissions: SharePermission[];
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  message: string | null;
  state: ShareState;
}

/** A link as its creator sees it later: only the first characters of the token, never the token. */
export interface ShareLink {
  id: string;
  versionId: string;
  versionLabel: string;
  tokenPrefix: string;
  label: string | null;
  createdBy: Person;
  permissions: SharePermission[];
  requiresPassword: boolean;
  createdAt: string;
  expiresAt: string;
  maxAccessCount: number | null;
  accessCount: number;
  lastAccessedAt: string | null;
  lockedUntil: string | null;
  revokedAt: string | null;
  state: ShareState;
}

export interface DocumentShares {
  shares: Share[];
  links: ShareLink[];
  canShare: boolean;
  canShareExternal: boolean;
  canManageAll: boolean;
}

export interface ReceivedShare {
  id: string;
  documentId: string;
  documentTitle: string;
  versionId: string;
  versionLabel: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  sharedBy: Person;
  permissions: SharePermission[];
  createdAt: string;
  expiresAt: string | null;
  message: string | null;
}

/** The token is in this response once; the server keeps only its digest. */
export interface CreatedShareLink {
  id: string;
  token: string;
  tokenPrefix: string;
  expiresAt: string;
}

/** One version with just enough of its document: what a share recipient sees. */
export interface VersionDetails {
  documentId: string;
  title: string;
  description: string | null;
  documentTypeId: string;
  version: DocumentVersion;
  allowedActions: string[];
}

export interface PublicLinkInfo {
  requiresPassword: boolean;
  lockedUntil: string | null;
}

export interface OpenedLink {
  sessionToken: string;
  sessionExpiresAt: string;
  title: string;
  versionLabel: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  canDownload: boolean;
  canPrint: boolean;
  previewStatus: RenditionStatus;
  pageCount: number;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    /** Per-field messages, keyed by field code or path (e.g. "fields[2].code"). */
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function toError(response: Response): Promise<ApiError> {
  const problem = await response.json().catch(() => ({}));
  return new ApiError(
    response.status,
    problem.code,
    problem.detail ?? problem.title ?? response.statusText,
    problem.errors ?? {},
  );
}

/** Exchanges the stored refresh token once, even when several requests hit a 401 together. */
async function refreshSession(): Promise<boolean> {
  const refreshToken = sessionStorage.getItem(refreshKey);
  if (!refreshToken) {
    return false;
  }

  refreshing ??= (async () => {
    try {
      const response = await fetch(`${baseUrl}/api/v1/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        sessionStorage.removeItem(refreshKey);
        accessToken = null;
        return false;
      }

      const tokens = (await response.json()) as AuthTokens;
      accessToken = tokens.accessToken;
      sessionStorage.setItem(refreshKey, tokens.refreshToken);
      return true;
    } finally {
      refreshing = null;
    }
  })();

  return refreshing;
}

async function send(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const response = await fetch(`${baseUrl}${path}`, { ...init, headers });
  if (response.status === 401 && retry && (await refreshSession())) {
    return send(path, init, false);
  }

  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }

  const text = search.toString();
  return text ? `?${text}` : '';
}

/** Pulls the file name out of Content-Disposition, preferring the RFC 5987 UTF-8 form. */
function fileNameFrom(header: string | null, fallback: string): string {
  if (!header) {
    return fallback;
  }

  const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (star) {
    return decodeURIComponent(star[1]);
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

/** Downloads a response as a file, keeping the server's file name. */
async function saveResponse(response: Response, fallbackName: string): Promise<void> {
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileNameFrom(response.headers.get('Content-Disposition'), fallbackName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/**
 * Calls for external links. Anonymous on purpose: a signed-in colleague opening a link must not
 * turn it into a request carrying their own token. The link session goes in a header, never in
 * the URL, so it stays out of logs and history.
 */
async function sendPublic(token: string, path: string, init: RequestInit = {}, session?: string): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body) {
    headers.set('Content-Type', 'application/json');
  }
  if (session) {
    headers.set('X-Share-Session', session);
  }

  return fetch(`${baseUrl}/api/v1/public/links/${encodeURIComponent(token)}${path}`, {
    ...init,
    headers,
    credentials: 'omit',
    referrerPolicy: 'no-referrer',
  });
}

async function publicRequest<T>(token: string, path: string, init: RequestInit = {}, session?: string): Promise<T> {
  const response = await sendPublic(token, path, init, session);
  if (!response.ok) {
    throw await toError(response);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  async login(username: string, password: string): Promise<AuthTokens> {
    const tokens = await request<AuthTokens>('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });

    accessToken = tokens.accessToken;
    sessionStorage.setItem(refreshKey, tokens.refreshToken);
    return tokens;
  },

  /** After a page reload the access token is gone; the refresh token brings the session back. */
  restoreSession: () => refreshSession(),

  me: () => request<CurrentUser>('/api/v1/auth/me'),

  async logout(): Promise<void> {
    const refreshToken = sessionStorage.getItem(refreshKey);
    if (refreshToken) {
      await request<void>('/api/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      }).catch(() => undefined);
    }

    accessToken = null;
    sessionStorage.removeItem(refreshKey);
  },

  categories: () => request<CategoryNode[]>('/api/v1/categories'),

  documentTypes: () => request<DocumentType[]>('/api/v1/document-types'),

  documents: (params: {
    categoryId?: string | null;
    includeSubcategories?: boolean;
    search?: string;
    page?: number;
    pageSize?: number;
  }) => request<Paged<DocumentListItem>>(`/api/v1/documents${query(params)}`),

  document: (id: string) => request<DocumentDetails>(`/api/v1/documents/${id}`),

  versions: (id: string) => request<DocumentVersion[]>(`/api/v1/documents/${id}/versions`),

  /** One version on its own; enough for someone who may see only that version (a share). */
  version: (documentId: string, versionId: string) =>
    request<VersionDetails>(`/api/v1/documents/${documentId}/versions/${versionId}`),

  recycleBin: (page = 1) => request<Paged<DocumentListItem>>(`/api/v1/recycle-bin${query({ page })}`),

  tags: (search: string) => request<Tag[]>(`/api/v1/tags${query({ search })}`),

  /**
   * Streams the file with XMLHttpRequest rather than fetch, because only XHR reports upload
   * progress, and on a phone a large upload without a progress bar looks like a hang.
   */
  upload(file: File, onProgress?: (fraction: number) => void): Promise<UploadResult> {
    const attempt = () =>
      new Promise<UploadResult>((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `${baseUrl}/api/v1/uploads`);
        if (accessToken) {
          xhr.setRequestHeader('Authorization', `Bearer ${accessToken}`);
        }

        xhr.upload.onprogress = (event) => {
          if (event.lengthComputable) {
            onProgress?.(event.loaded / event.total);
          }
        };

        xhr.onload = () => {
          const body = xhr.responseText ? JSON.parse(xhr.responseText) : {};
          if (xhr.status >= 200 && xhr.status < 300) {
            resolve(body as UploadResult);
          } else {
            reject(new ApiError(xhr.status, body.code, body.detail ?? body.title ?? xhr.statusText, body.errors ?? {}));
          }
        };

        xhr.onerror = () => reject(new ApiError(0, 'network', 'network'));

        const form = new FormData();
        form.append('file', file, file.name);
        xhr.send(form);
      });

    return attempt().catch(async (error: unknown) => {
      if (error instanceof ApiError && error.status === 401 && (await refreshSession())) {
        return attempt();
      }

      throw error;
    });
  },

  createDocument(body: {
    title: string;
    description: string | null;
    categoryId: string;
    documentTypeId: string;
    uploadId: string;
    tags: string[];
    changeDescription: string | null;
    metadata: Metadata;
  }, idempotencyKey: string): Promise<CreatedVersion> {
    return request<CreatedVersion>('/api/v1/documents', {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  },

  addVersion(
    documentId: string,
    body: { uploadId: string; changeDescription: string | null; baseVersionId: string | null },
    idempotencyKey: string,
  ): Promise<CreatedVersion> {
    return request<CreatedVersion>(`/api/v1/documents/${documentId}/versions`, {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  },

  updateMetadata: (
    id: string,
    body: { metadata: Metadata; changeDescription: string | null; baseVersionId: string | null; upgradeSchema: boolean },
    idempotencyKey: string,
  ) =>
    request<MetadataUpdate>(`/api/v1/documents/${id}/metadata`, {
      method: 'PUT',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    }),

  /** The latest published schema of a type: the form for a new document. */
  latestSchema: (documentTypeId: string) =>
    request<DocumentTypeSchema>(`/api/v1/document-types/${documentTypeId}/schema`),

  /** One schema version; published schemas never change, so callers may cache forever. */
  schema: (versionId: string) => request<DocumentTypeSchema>(`/api/v1/document-types/schemas/${versionId}`),

  users: (params: { search?: string; ids?: string[] }) =>
    request<DirectoryEntry[]>(`/api/v1/directory/users${query({ search: params.search, ids: params.ids?.join(',') })}`),

  groups: (params: { search?: string; ids?: string[] }) =>
    request<DirectoryEntry[]>(`/api/v1/directory/groups${query({ search: params.search, ids: params.ids?.join(',') })}`),

  workflow: {
    tasks: () => request<WorkflowTask[]>('/api/v1/workflow/tasks'),

    forDocument: (documentId: string) => request<WorkflowInstance[]>(`/api/v1/documents/${documentId}/workflow`),

    start: (documentId: string, versionId: string) =>
      request<{ instanceId: string }>(`/api/v1/documents/${documentId}/versions/${versionId}/workflow/start`, { method: 'POST' }),

    act: (
      taskId: string,
      action: WorkflowAction,
      body: { comment: string | null; targetStepCode?: string | null; forwardToUserId?: string | null },
    ) => {
      const route = { Approve: 'approve', Reject: 'reject', Return: 'return', RequestChanges: 'request-changes', Forward: 'forward' }[action];
      return request<void>(`/api/v1/workflow/tasks/${taskId}/${route}`, { method: 'POST', body: JSON.stringify(body) });
    },

    cancel: (instanceId: string, reason: string) =>
      request<void>(`/api/v1/workflow/instances/${instanceId}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),

    definitions: () => request<WorkflowDefinition[]>('/api/v1/workflow/definitions'),
  },

  admin: {
    roles: () => request<RoleSummary[]>('/api/v1/admin/roles'),

    workflow: (id: string) => request<WorkflowAdmin>(`/api/v1/admin/workflows/${id}`),

    createWorkflow: (body: { code: string; name: string; description: string | null }) =>
      request<{ id: string }>('/api/v1/admin/workflows', { method: 'POST', body: JSON.stringify(body) }),

    updateWorkflow: (id: string, body: { name: string; description: string | null; isActive: boolean }) =>
      request<void>(`/api/v1/admin/workflows/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

    saveWorkflowDraft: (id: string, steps: WorkflowStep[]) =>
      request<void>(`/api/v1/admin/workflows/${id}/draft`, { method: 'PUT', body: JSON.stringify({ steps }) }),

    publishWorkflow: (id: string) =>
      request<{ versionId: string }>(`/api/v1/admin/workflows/${id}/publish`, { method: 'POST' }),

    documentTypes: () => request<DocumentType[]>('/api/v1/document-types?includeInactive=true'),

    documentType: (id: string) => request<DocumentTypeAdmin>(`/api/v1/admin/document-types/${id}`),

    createDocumentType: (body: { code: string; name: string; description: string | null }) =>
      request<{ id: string }>('/api/v1/admin/document-types', { method: 'POST', body: JSON.stringify(body) }),

    updateDocumentType: (
      id: string,
      body: { name: string; description: string | null; settings: DocumentTypeSettings; isActive: boolean },
    ) => request<void>(`/api/v1/admin/document-types/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

    saveDraft: (id: string, body: { fields: FieldSchema[]; rules: FieldRuleSchema[] }) =>
      request<void>(`/api/v1/admin/document-types/${id}/draft`, { method: 'PUT', body: JSON.stringify(body) }),

    publish: (id: string) =>
      request<{ versionId: string }>(`/api/v1/admin/document-types/${id}/publish`, { method: 'POST' }),
  },

  updateDocument: (id: string, body: { title: string; description: string | null; categoryId: string }) =>
    request<void>(`/api/v1/documents/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

  setTags: (id: string, tags: string[]) =>
    request<void>(`/api/v1/documents/${id}/tags`, { method: 'PUT', body: JSON.stringify({ tags }) }),

  deleteDocument: (id: string, reason: string) =>
    request<void>(`/api/v1/documents/${id}${query({ reason })}`, { method: 'DELETE' }),

  restoreDocument: (id: string) => request<void>(`/api/v1/documents/${id}/restore`, { method: 'POST' }),

  /**
   * Downloads through the API so the bearer token, the permission check and the audit record
   * all apply; the browser never sees a storage URL.
   */
  async download(documentId: string, versionId: string | null, fallbackName: string): Promise<void> {
    const path = versionId
      ? `/api/v1/documents/${documentId}/versions/${versionId}/content`
      : `/api/v1/documents/${documentId}/content`;

    const response = await send(path);
    if (!response.ok) {
      throw await toError(response);
    }

    await saveResponse(response, fallbackName);
  },

  search: (params: SearchParams) => request<SearchResult>(`/api/v1/search${query({ ...params })}`),

  preview: {
    /** Opens the viewer (audited once as DOCUMENT_VIEWED); the effective version by default. */
    open: (documentId: string, versionId?: string | null) =>
      request<PreviewInfo>(`/api/v1/documents/${documentId}/preview${query({ versionId })}`, { method: 'POST' }),

    /** Records DOCUMENT_PRINTED; call before fetching print pages. */
    startPrint: (documentId: string, versionId: string) =>
      request<PreviewInfo>(`/api/v1/documents/${documentId}/versions/${versionId}/print`, { method: 'POST' }),

    /**
     * A page image as an object URL. Fetched with the bearer token, so an <img src> pointing at
     * the API would not work; the caller revokes the URL when the page leaves the screen.
     */
    async page(documentId: string, versionId: string, page: number, purpose: 'view' | 'print' = 'view'): Promise<string> {
      const segment = purpose === 'print' ? 'print' : 'pages';
      const response = await send(`/api/v1/documents/${documentId}/versions/${versionId}/${segment}/${page}`);
      if (!response.ok) {
        throw await toError(response);
      }

      return URL.createObjectURL(await response.blob());
    },

    async thumbnail(documentId: string): Promise<string | null> {
      const response = await send(`/api/v1/documents/${documentId}/thumbnail`);
      return response.ok ? URL.createObjectURL(await response.blob()) : null;
    },

    reprocess: (documentId: string, versionId: string) =>
      request<void>(`/api/v1/documents/${documentId}/versions/${versionId}/reprocess`, { method: 'POST' }),
  },

  sharing: {
    forDocument: (documentId: string) => request<DocumentShares>(`/api/v1/documents/${documentId}/shares`),

    received: () => request<ReceivedShare[]>('/api/v1/shares/received'),

    share: (
      documentId: string,
      body: { versionId: string; recipientId: string; permissions: SharePermission[]; expiresAt: string | null; message: string | null },
    ) => request<{ id: string }>(`/api/v1/documents/${documentId}/shares`, { method: 'POST', body: JSON.stringify(body) }),

    revoke: (shareId: string) => request<void>(`/api/v1/shares/${shareId}`, { method: 'DELETE' }),

    createLink: (
      documentId: string,
      body: {
        versionId: string;
        permissions: SharePermission[];
        expiresAt: string;
        maxAccessCount: number | null;
        password: string | null;
        label: string | null;
      },
    ) => request<CreatedShareLink>(`/api/v1/documents/${documentId}/links`, { method: 'POST', body: JSON.stringify(body) }),

    revokeLink: (linkId: string) => request<void>(`/api/v1/share-links/${linkId}`, { method: 'DELETE' }),
  },

  publicLink: {
    info: (token: string) => publicRequest<PublicLinkInfo>(token, ''),

    /** Spends one opening of the link. */
    open: (token: string, password: string | null) =>
      publicRequest<OpenedLink>(token, '/open', { method: 'POST', body: JSON.stringify({ password }) }),

    async page(token: string, session: string, page: number, purpose: 'view' | 'print' = 'view'): Promise<string> {
      const response = await sendPublic(token, `/${purpose === 'print' ? 'print' : 'pages'}/${page}`, {}, session);
      if (!response.ok) {
        throw await toError(response);
      }

      return URL.createObjectURL(await response.blob());
    },

    startPrint: (token: string, session: string) => publicRequest<void>(token, '/print', { method: 'POST' }, session),

    async download(token: string, session: string, fallbackName: string): Promise<void> {
      const response = await sendPublic(token, '/content', {}, session);
      if (!response.ok) {
        throw await toError(response);
      }

      await saveResponse(response, fallbackName);
    },
  },

  searchAdmin: {
    status: () => request<SearchStatus>('/api/v1/admin/search/status'),
    reindex: () => request<void>('/api/v1/admin/search/reindex', { method: 'POST' }),
    retryFailed: () => request<{ queued: number }>('/api/v1/admin/search/retry-failed', { method: 'POST' }),
  },

  isSignedIn: () => accessToken !== null,
};
