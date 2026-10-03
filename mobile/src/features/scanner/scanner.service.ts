import type { ScanPage } from './scanner.types';

export function addPage(pages: ScanPage[], page: ScanPage): ScanPage[] {
  return [...pages, page];
}

export function deletePage(pages: ScanPage[], id: string): ScanPage[] {
  return pages.filter((page) => page.id !== id);
}

/** Moves one page earlier (`-1`) or later (`1`) in document order. */
export function movePage(pages: ScanPage[], id: string, direction: -1 | 1): ScanPage[] {
  const index = pages.findIndex((page) => page.id === id);
  const target = index + direction;
  if (index < 0 || target < 0 || target >= pages.length) return pages;
  const next = pages.slice();
  const [page] = next.splice(index, 1);
  if (!page) return pages;
  next.splice(target, 0, page);
  return next;
}
