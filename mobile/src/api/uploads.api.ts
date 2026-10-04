import { Platform } from 'react-native';
import { http, withApi } from './client';
import type { PreparedFile } from '../features/scanner/filing';
import type { UploadResult } from '../types/document';

async function appendUploadPart(body: FormData, file: PreparedFile): Promise<void> {
  if (Platform.OS === 'web') {
    const response = await fetch(file.uri);
    if (!response.ok) {
      throw new Error('خواندن فایل برای آپلود ناموفق بود.');
    }
    const blob = await response.blob();
    body.append('file', new File([blob], file.fileName, { type: file.mimeType }));
    return;
  }

  body.append('file', {
    uri: file.uri,
    name: file.fileName,
    type: file.mimeType,
  } as unknown as Blob);
}

/**
 * `POST /api/v1/uploads`. The multipart part name is `file`.
 * The server sniffs the bytes; the declared name/type are hints only.
 */
export function uploadFile(file: PreparedFile, onProgress?: (fraction: number) => void): Promise<UploadResult> {
  return withApi(async () => {
    const body = new FormData();
    await appendUploadPart(body, file);
    const response = await http.post<UploadResult>('/uploads', body, {
      transformRequest: (data) => data,
      onUploadProgress: (event) => {
        if (event.total) onProgress?.(event.loaded / event.total);
      },
    });
    return response.data;
  });
}

/** @deprecated Prefer uploadFile — kept for older call sites/tests. */
export function uploadPdf(uri: string, onProgress?: (fraction: number) => void): Promise<UploadResult> {
  return uploadFile({ uri, fileName: 'scan.pdf', mimeType: 'application/pdf' }, onProgress);
}
