import type { CreateDocumentBody, CreatedVersion, UploadResult } from '../../types/document';
import type { ScanPage } from './scanner.types';

export type FilingFormat = 'pdf' | 'image';
export type FilingPhase = 'prepare' | 'upload' | 'create';

export type FilingOutcome =
  | { status: 'created'; document: CreatedVersion }
  | { status: 'duplicate'; duplicates: UploadResult['duplicates']; uploadId: string; fileUri: string };

export interface PreparedFile {
  uri: string;
  fileName: string;
  mimeType: string;
}

export interface FilingDeps {
  prepareFile(pages: ScanPage[], format: FilingFormat): Promise<PreparedFile>;
  upload(file: PreparedFile, onProgress: (fraction: number) => void): Promise<UploadResult>;
  createDocument(body: CreateDocumentBody, idempotencyKey: string): Promise<CreatedVersion>;
}

export async function fileScan(
  deps: FilingDeps,
  input: {
    pages: ScanPage[];
    format: FilingFormat;
    fileUri?: string;
    fileName?: string;
    mimeType?: string;
    uploadId?: string;
    draft: Omit<CreateDocumentBody, 'uploadId'>;
    idempotencyKey: string;
    onPhase?: (phase: FilingPhase) => void;
    onProgress?: (fraction: number) => void;
    onPrepared?: (file: PreparedFile) => void;
    onUploaded?: (uploadId: string) => void;
  },
): Promise<FilingOutcome & { fileUri: string; uploadId: string }> {
  if (input.format === 'image' && input.pages.length !== 1) {
    throw new Error('ثبت به‌صورت عکس فقط برای یک صفحه ممکن است.');
  }

  let file: PreparedFile | undefined =
    input.fileUri && input.fileName && input.mimeType
      ? { uri: input.fileUri, fileName: input.fileName, mimeType: input.mimeType }
      : undefined;

  if (!file) {
    input.onPhase?.('prepare');
    file = await deps.prepareFile(input.pages, input.format);
    input.onPrepared?.(file);
  }

  let uploadId = input.uploadId;
  let duplicates: UploadResult['duplicates'] = [];
  if (!uploadId) {
    input.onPhase?.('upload');
    const uploaded = await deps.upload(file, input.onProgress ?? (() => undefined));
    uploadId = uploaded.uploadId;
    duplicates = uploaded.duplicates;
    if (duplicates.length === 0) input.onUploaded?.(uploadId);
  }

  if (duplicates.length > 0) {
    return { status: 'duplicate', duplicates, uploadId, fileUri: file.uri };
  }

  input.onPhase?.('create');
  const document = await deps.createDocument({ ...input.draft, uploadId }, input.idempotencyKey);
  return { status: 'created', document, fileUri: file.uri, uploadId };
}
