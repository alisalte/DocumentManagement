import { PDFDocument } from 'pdf-lib';
import { imageMime } from './pdf-html';
import { decodeBase64, encodeBase64, readUriBytes, writeCacheFile } from './bytes';
import { newScanId } from './scan-id';
import type { ScanPage } from './scanner.types';

export interface PdfIo {
  readBytes(uri: string): Promise<Uint8Array>;
  writePdf(bytes: Uint8Array): Promise<string>;
}

const defaultIo: PdfIo = {
  readBytes: readUriBytes,
  writePdf: (bytes) => writeCacheFile(bytes, `scan-${newScanId()}.pdf`, 'application/pdf'),
};

/**
 * Builds a real multi-page PDF on every platform.
 * `expo-print` on web only opens the browser print dialog and does not yield a file,
 * which made filing from the phone browser fail with a server error.
 */
export async function createPdfFromPages(pages: ScanPage[], io: PdfIo = defaultIo): Promise<string> {
  if (pages.length === 0) {
    throw new Error('At least one page is required.');
  }

  const pdf = await PDFDocument.create();
  for (const page of pages) {
    const source = await io.readBytes(page.uri);
    const mime = imageMime(page.uri);
    const imageBytes = mime === 'image/webp' ? await webpToJpeg(source) : source;
    const image =
      mime === 'image/png'
        ? await pdf.embedPng(imageBytes)
        : await pdf.embedJpg(imageBytes);
    const pdfPage = pdf.addPage([image.width, image.height]);
    pdfPage.drawImage(image, { x: 0, y: 0, width: image.width, height: image.height });
  }

  return io.writePdf(await pdf.save());
}

/** Convert WebP (unsupported by pdf-lib) to JPEG when a canvas is available. */
async function webpToJpeg(bytes: Uint8Array): Promise<Uint8Array> {
  if (typeof document === 'undefined' || typeof Image === 'undefined') {
    throw new Error('فرمت WebP روی این دستگاه برای ساخت PDF پشتیبانی نمی‌شود. عکس را به‌صورت JPEG بگیرید.');
  }

  const blob = new Blob([bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer], {
    type: 'image/webp',
  });
  const url = URL.createObjectURL(blob);
  try {
    const image = await loadHtmlImage(url);
    const canvas = document.createElement('canvas');
    canvas.width = image.naturalWidth || image.width;
    canvas.height = image.naturalHeight || image.height;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas is not available.');
    context.drawImage(image, 0, 0);
    const dataUrl = canvas.toDataURL('image/jpeg', 0.85);
    return decodeBase64(dataUrl);
  } finally {
    URL.revokeObjectURL(url);
  }
}

function loadHtmlImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('خواندن تصویر ناموفق بود.'));
    image.src = url;
  });
}

export { encodeBase64 };
