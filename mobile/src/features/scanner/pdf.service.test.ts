import { describe, expect, it } from 'vitest';
import { PDFDocument } from 'pdf-lib';
import { decodeBase64 } from './bytes';
import { buildPdfHtml } from './pdf-html';
import { createPdfFromPages } from './pdf.service';
import type { ScanPage } from './scanner.types';

/** Minimal valid 1×1 PNG. */
const tinyPng = decodeBase64(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
);

function page(id: string): ScanPage {
  return { id, uri: `file:///tmp/${id}.png`, width: 10, height: 20 };
}

describe('pdf generation', () => {
  it('builds a one-page document without a trailing page break', () => {
    const html = buildPdfHtml([{ dataUri: 'data:image/jpeg;base64,AAA' }]);
    expect(html.match(/<section class="page">/g)).toHaveLength(1);
    expect(html).toContain('data:image/jpeg;base64,AAA');
    expect(html).toContain('.page:last-child');
  });

  it('builds one section per page', () => {
    const html = buildPdfHtml([
      { dataUri: 'data:image/jpeg;base64,ONE' },
      { dataUri: 'data:image/png;base64,TWO' },
    ]);
    expect(html.match(/<section class="page">/g)).toHaveLength(2);
    expect(html.indexOf('ONE')).toBeLessThan(html.indexOf('TWO'));
  });

  it('writes a real one-page pdf from a png', async () => {
    const written: Uint8Array[] = [];
    const uri = await createPdfFromPages([page('a')], {
      readBytes: async () => tinyPng,
      writePdf: async (bytes) => {
        written.push(bytes);
        return 'file:///tmp/one.pdf';
      },
    });
    expect(uri).toBe('file:///tmp/one.pdf');
    expect(written).toHaveLength(1);
    const pdf = await PDFDocument.load(written[0]!);
    expect(pdf.getPageCount()).toBe(1);
  });

  it('writes a multi-page pdf in page order', async () => {
    const written: Uint8Array[] = [];
    await createPdfFromPages([page('a'), page('b')], {
      readBytes: async () => tinyPng,
      writePdf: async (bytes) => {
        written.push(bytes);
        return 'file:///tmp/many.pdf';
      },
    });
    const pdf = await PDFDocument.load(written[0]!);
    expect(pdf.getPageCount()).toBe(2);
  });
});
