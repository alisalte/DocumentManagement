import { http, withApi } from './client';
import type { UploadResult } from '../types/document';

/**
 * `POST /api/v1/uploads`. The multipart part name is `file`.
 * The server sniffs the bytes; a real PDF is stored as `application/pdf`.
 */
export function uploadPdf(uri: string, onProgress?: (fraction: number) => void): Promise<UploadResult> {
  const body = new FormData();
  body.append('file', {
    uri,
    name: 'scan.pdf',
    type: 'application/pdf',
  } as unknown as Blob);

  return withApi(async () => {
    const response = await http.post<UploadResult>('/uploads', body, {
      transformRequest: (data) => data,
      onUploadProgress: (event) => {
        if (event.total) onProgress?.(event.loaded / event.total);
      },
    });
    return response.data;
  });
}
