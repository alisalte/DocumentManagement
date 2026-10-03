import type { CreateDocumentBody, CreatedVersion, UploadResult } from '../../types/document';
import type { ScanPage } from './scanner.types';

export type FilingPhase = 'pdf' | 'upload' | 'create';

export type FilingOutcome =
  | { status: 'created'; document: CreatedVersion }
  | { status: 'duplicate'; duplicates: UploadResult['duplicates']; uploadId: string; pdfUri: string };

export interface FilingDeps {
  createPdf(pages: ScanPage[]): Promise<string>;
  upload(uri: string, onProgress: (fraction: number) => void): Promise<UploadResult>;
  createDocument(body: CreateDocumentBody, idempotencyKey: string): Promise<CreatedVersion>;
}

export async function fileScan(
  deps: FilingDeps,
  input: {
    pages: ScanPage[];
    pdfUri?: string;
    uploadId?: string;
    draft: Omit<CreateDocumentBody, 'uploadId'>;
    idempotencyKey: string;
    onPhase?: (phase: FilingPhase) => void;
    onProgress?: (fraction: number) => void;
    onPdf?: (uri: string) => void;
    onUploaded?: (uploadId: string) => void;
  },
): Promise<FilingOutcome & { pdfUri: string; uploadId: string }> {
  let pdfUri = input.pdfUri;
  if (!pdfUri) {
    input.onPhase?.('pdf');
    pdfUri = await deps.createPdf(input.pages);
    input.onPdf?.(pdfUri);
  }

  let uploadId = input.uploadId;
  let duplicates: UploadResult['duplicates'] = [];
  if (!uploadId) {
    input.onPhase?.('upload');
    const uploaded = await deps.upload(pdfUri, input.onProgress ?? (() => undefined));
    uploadId = uploaded.uploadId;
    duplicates = uploaded.duplicates;
    if (duplicates.length === 0) input.onUploaded?.(uploadId);
  }

  if (duplicates.length > 0) {
    return { status: 'duplicate', duplicates, uploadId, pdfUri };
  }

  input.onPhase?.('create');
  const document = await deps.createDocument({ ...input.draft, uploadId }, input.idempotencyKey);
  return { status: 'created', document, pdfUri, uploadId };
}
