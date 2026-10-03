export interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  code: string;
  depth: number;
  isActive: boolean;
  canCreate: boolean;
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
  categoryName: string;
  status: string;
}
