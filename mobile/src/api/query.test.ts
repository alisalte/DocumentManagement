import { describe, expect, it } from 'vitest';
import { toQuery } from './query';

describe('toQuery', () => {
  it('omits empty values and prefixes with ?', () => {
    expect(toQuery({ q: 'نامه', page: 1, tag: null, empty: '' })).toBe('?q=%D9%86%D8%A7%D9%85%D9%87&page=1');
  });

  it('returns an empty string when nothing is set', () => {
    expect(toQuery({ q: undefined, page: null })).toBe('');
  });
});
