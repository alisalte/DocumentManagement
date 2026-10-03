import { http, withApi } from './client';
import { toQuery } from './query';
import type {
  CategoryNode,
  CreateDocumentBody,
  CreatedVersion,
  DocumentDetails,
  DocumentListItem,
  DocumentType,
  DocumentTypeSchema,
  Paged,
} from '../types/document';

export function listCategories(): Promise<CategoryNode[]> {
  return withApi(async () => (await http.get<CategoryNode[]>('/categories')).data);
}

export function listDocumentTypes(): Promise<DocumentType[]> {
  return withApi(async () => (await http.get<DocumentType[]>('/document-types')).data);
}

export function getSchema(documentTypeId: string): Promise<DocumentTypeSchema> {
  return withApi(async () => (await http.get<DocumentTypeSchema>(`/document-types/${documentTypeId}/schema`)).data);
}

export function listDocuments(params: {
  categoryId?: string | null;
  includeSubcategories?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<Paged<DocumentListItem>> {
  return withApi(async () => (await http.get<Paged<DocumentListItem>>(`/documents${toQuery({ ...params })}`)).data);
}

export function getDocument(id: string): Promise<DocumentDetails> {
  return withApi(async () => (await http.get<DocumentDetails>(`/documents/${id}`)).data);
}

/** `POST /api/v1/documents`. `Idempotency-Key` makes a retry of the same body safe. */
export function createDocument(body: CreateDocumentBody, idempotencyKey: string): Promise<CreatedVersion> {
  return withApi(async () => {
    const response = await http.post<CreatedVersion>('/documents', body, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
    return response.data;
  });
}
