import { describe, expect, it, vi } from 'vitest';
import { buildPdfHtml } from './pdf-html';
import { createPdfFromPages } from './pdf.service';
import type { ScanPage } from './scanner.types';

function page(id: string, ext = 'jpg'): ScanPage {
  return { id, uri: `file:///tmp/${id}.${ext}`, width: 10, height: 20 };
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

  it('prints a one-page pdf from a single image', async () => {
    const printHtml = vi.fn(async (_html: string) => 'file:///tmp/one.pdf');
    const uri = await createPdfFromPages([page('a')], {
      readBase64: async () => 'QUJD',
      printHtml,
    });
    expect(uri).toBe('file:///tmp/one.pdf');
    const html = printHtml.mock.calls[0]?.[0] ?? '';
    expect(html.match(/<section class="page">/g)).toHaveLength(1);
    expect(html).toContain('data:image/jpeg;base64,QUJD');
  });

  it('prints a multi-page pdf in page order', async () => {
    const printHtml = vi.fn(async (_html: string) => 'file:///tmp/many.pdf');
    await createPdfFromPages([page('a'), page('b', 'png')], {
      readBase64: async (uri) => (uri.endsWith('.png') ? 'PNG' : 'JPG'),
      printHtml,
    });
    const html = printHtml.mock.calls[0]?.[0] ?? '';
    expect(html.match(/<section class="page">/g)).toHaveLength(2);
    expect(html).toContain('data:image/jpeg;base64,JPG');
    expect(html).toContain('data:image/png;base64,PNG');
    expect(html.indexOf('JPG')).toBeLessThan(html.indexOf('PNG'));
  });
});
