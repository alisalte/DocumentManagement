import { describe, expect, it } from 'vitest';
import { parseHighlight } from './highlight';

describe('parseHighlight', () => {
  it('splits a fragment into plain and marked text', () => {
    expect(parseHighlight('قرارداد <mark>خرید</mark> تجهیزات')).toEqual([
      { text: 'قرارداد ', marked: false },
      { text: 'خرید', marked: true },
      { text: ' تجهیزات', marked: false },
    ]);
  });

  it('never turns document text into markup', () => {
    const segments = parseHighlight('&lt;script&gt;alert(1)&lt;/script&gt; <mark>x</mark>');
    expect(segments[0]).toEqual({ text: '<script>alert(1)</script> ', marked: false });
    expect(segments.every((segment) => typeof segment.text === 'string')).toBe(true);
  });

  it('decodes each entity once, not twice', () => {
    expect(parseHighlight('&amp;lt;')).toEqual([{ text: '&lt;', marked: false }]);
  });
});
