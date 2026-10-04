import { imageMime } from './pdf-html';
import { createPdfFromPages } from './pdf.service';
import { extensionOf } from './scan-id';
import type { FilingFormat, PreparedFile } from './filing';
import type { ScanPage } from './scanner.types';

/** Build the file that will be uploaded: a PDF of all pages, or the single page image. */
export async function prepareScanFile(pages: ScanPage[], format: FilingFormat): Promise<PreparedFile> {
  if (format === 'image') {
    if (pages.length !== 1) {
      throw new Error('ثبت به‌صورت عکس فقط برای یک صفحه ممکن است.');
    }
    const page = pages[0]!;
    const mimeType = imageMime(page.uri);
    const ext = extensionOf(page.uri);
    return {
      uri: page.uri,
      fileName: `scan.${ext === 'jpg' ? 'jpg' : ext}`,
      mimeType,
    };
  }

  const uri = await createPdfFromPages(pages);
  return { uri, fileName: 'scan.pdf', mimeType: 'application/pdf' };
}
