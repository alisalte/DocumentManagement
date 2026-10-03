import { describe, expect, it } from 'vitest';
import { addPage, deletePage, movePage } from './scanner.service';
import type { ScanPage } from './scanner.types';

function page(id: string): ScanPage {
  return { id, uri: `file://${id}.jpg`, width: 100, height: 200 };
}

describe('scanner pages', () => {
  it('adds a page at the end', () => {
    expect(addPage([page('a')], page('b')).map((item) => item.id)).toEqual(['a', 'b']);
  });

  it('deletes a page and leaves the others in order', () => {
    expect(deletePage([page('a'), page('b'), page('c')], 'b').map((item) => item.id)).toEqual(['a', 'c']);
  });

  it('reorders a page earlier or later', () => {
    const pages = [page('a'), page('b'), page('c')];
    expect(movePage(pages, 'c', -1).map((item) => item.id)).toEqual(['a', 'c', 'b']);
    expect(movePage(pages, 'a', 1).map((item) => item.id)).toEqual(['b', 'a', 'c']);
    expect(movePage(pages, 'a', -1)).toBe(pages);
    expect(movePage(pages, 'c', 1)).toBe(pages);
  });
});
