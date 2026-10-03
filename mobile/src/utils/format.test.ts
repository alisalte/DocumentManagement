import { describe, expect, it } from 'vitest';
import { faDigits, formatBytes, formatDateFa } from './format';

describe('format helpers', () => {
  it('maps ASCII digits to Persian', () => {
    expect(faDigits('12')).toBe('۱۲');
  });

  it('formats byte sizes', () => {
    expect(formatBytes(null)).toBe('—');
    expect(formatBytes(512)).toBe('۵۱۲ B');
    expect(formatBytes(2048)).toBe('۲.۰ KB');
  });

  it('formats dates or falls back', () => {
    expect(formatDateFa(undefined)).toBe('—');
    expect(formatDateFa('not-a-date')).toBe('not-a-date'.slice(0, 10));
  });
});
