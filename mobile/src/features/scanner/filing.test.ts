import { describe, expect, it, vi } from 'vitest';
import { fileScan } from './filing';
import type { ScanPage } from './scanner.types';
import { ApiError } from '../../utils/errors';

const pages: ScanPage[] = [{ id: 'p1', uri: 'file:///a.jpg', width: 1, height: 1 }];
const draft = {
  title: 'نامه',
  description: null,
  categoryId: '11111111-1111-1111-1111-111111111111',
  documentTypeId: '22222222-2222-2222-2222-222222222222',
  tags: [],
  changeDescription: 'اسکن با موبایل',
  metadata: {},
};

describe('filing', () => {
  it('uploads the pdf and then creates the document', async () => {
    const createPdf = vi.fn(async () => 'file:///scan.pdf');
    const upload = vi.fn(async () => ({
      uploadId: 'upload-1',
      fileName: 'scan.pdf',
      mimeType: 'application/pdf',
      size: 10,
      sha256: 'abc',
      duplicates: [],
    }));
    const createDocument = vi.fn(async () => ({
      documentId: 'doc-1',
      versionId: 'ver-1',
      label: 'V1.1',
    }));

    const outcome = await fileScan(
      { createPdf, upload, createDocument },
      { pages, draft, idempotencyKey: 'key-1' },
    );

    expect(outcome.status).toBe('created');
    if (outcome.status === 'created') {
      expect(outcome.document.label).toBe('V1.1');
    }
    expect(upload).toHaveBeenCalledWith('file:///scan.pdf', expect.any(Function));
    expect(createDocument).toHaveBeenCalledWith(expect.objectContaining({ uploadId: 'upload-1', title: 'نامه' }), 'key-1');
  });

  it('reports upload failure and does not create a document', async () => {
    const createDocument = vi.fn();
    await expect(
      fileScan(
        {
          createPdf: async () => 'file:///scan.pdf',
          upload: async () => {
            throw new ApiError(0, 'network', 'network');
          },
          createDocument,
        },
        { pages, draft, idempotencyKey: 'key-1' },
      ),
    ).rejects.toBeInstanceOf(ApiError);
    expect(createDocument).not.toHaveBeenCalled();
  });

  it('reports document creation failure after a successful upload', async () => {
    const onUploaded = vi.fn();
    await expect(
      fileScan(
        {
          createPdf: async () => 'file:///scan.pdf',
          upload: async () => ({
            uploadId: 'upload-1',
            fileName: 'scan.pdf',
            mimeType: 'application/pdf',
            size: 10,
            sha256: 'abc',
            duplicates: [],
          }),
          createDocument: async () => {
            throw new ApiError(403, 'auth.forbidden', 'no');
          },
        },
        { pages, draft, idempotencyKey: 'key-1', onUploaded },
      ),
    ).rejects.toMatchObject({ status: 403 });
    expect(onUploaded).toHaveBeenCalledWith('upload-1');
  });

  it('stops when the upload is a visible duplicate', async () => {
    const createDocument = vi.fn();
    const outcome = await fileScan(
      {
        createPdf: async () => 'file:///scan.pdf',
        upload: async () => ({
          uploadId: 'upload-1',
          fileName: 'scan.pdf',
          mimeType: 'application/pdf',
          size: 10,
          sha256: 'abc',
          duplicates: [{ documentId: 'doc-9', title: 'نامه قبلی' }],
        }),
        createDocument,
      },
      { pages, draft, idempotencyKey: 'key-1' },
    );
    expect(outcome.status).toBe('duplicate');
    expect(createDocument).not.toHaveBeenCalled();
  });
});
