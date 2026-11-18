import { describe, expect, it } from 'vitest';
import { pageWindow } from './Pagination';

describe('pageWindow', () => {
  it('lists every page when there are few', () => {
    expect(pageWindow(5, 3)).toEqual([1, 2, 3, 4, 5]);
  });

  it('keeps the ends and the neighbours of the current page', () => {
    expect(pageWindow(20, 10)).toEqual([1, 'gap', 9, 10, 11, 'gap', 20]);
  });

  it('shows a lone hidden page instead of a gap', () => {
    expect(pageWindow(7, 4)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(pageWindow(10, 1)).toEqual([1, 2, 'gap', 10]);
  });
});
