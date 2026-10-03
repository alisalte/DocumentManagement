import type { ScanPage } from './scanner.types';
import { buildPdfHtml, imageMime } from './pdf-html';

export interface PdfIo {
  readBase64(uri: string): Promise<string>;
  printHtml(html: string): Promise<string>;
}

const defaultIo: PdfIo = {
  async readBase64(uri) {
    const { File } = await import('expo-file-system');
    return new File(uri).base64();
  },
  async printHtml(html) {
    const Print = await import('expo-print');
    const result = await Print.printToFileAsync({ html, width: 595, height: 842 });
    return result.uri;
  },
};

/** Images → HTML → a local multi-page PDF. Returns the file URI. */
export async function createPdfFromPages(pages: ScanPage[], io: PdfIo = defaultIo): Promise<string> {
  if (pages.length === 0) {
    throw new Error('At least one page is required.');
  }

  const images = [];
  for (const page of pages) {
    const base64 = await io.readBase64(page.uri);
    images.push({ dataUri: `data:${imageMime(page.uri)};base64,${base64}` });
  }

  return io.printHtml(buildPdfHtml(images));
}
