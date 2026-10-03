export interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  code: string;
  depth: number;
  isActive: boolean;
  canView?: boolean;
  canCreate: boolean;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
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

export interface DocumentType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  latestPublishedVersionId: string | null;
}

export interface LocalizedText {
  fa: string;
  en?: string | null;
}

export interface FieldOption {
  value: string;
  label: LocalizedText;
  isActive: boolean;
}

export interface FieldSchema {
  code: string;
  label: LocalizedText;
  type: string;
  isRequired: boolean;
  isActive: boolean;
  options: FieldOption[];
}

export interface DocumentTypeSchema {
  documentTypeId: string;
  versionId: string;
  fields: FieldSchema[];
}

export type MetadataValue = string | number | boolean | null;

export interface UploadResult {
  uploadId: string;
  fileName: string;
  mimeType: string;
  size: number;
  sha256: string;
  duplicates: { documentId: string; title: string }[];
}

export interface CreateDocumentBody {
  title: string;
  description: string | null;
  categoryId: string;
  documentTypeId: string;
  uploadId: string;
  tags: string[];
  changeDescription: string | null;
  metadata: Record<string, MetadataValue>;
}

/** `CreatedVersionDto`: there is no separate document number in this API. */
export interface CreatedVersion {
  documentId: string;
  versionId: string;
  label: string;
}

export interface DocumentDetails {
  id: string;
  title: string;
  description: string | null;
  categoryId: string;
  categoryName: string;
  documentTypeId: string;
  currentVersionId: string | null;
  currentVersionLabel?: string | null;
  latestVersionNumber?: number;
  status: string;
  tags?: Tag[];
  updatedAt?: string;
  createdAt?: string;
  fileName?: string | null;
  mimeType?: string | null;
  fileSize?: number | null;
}
