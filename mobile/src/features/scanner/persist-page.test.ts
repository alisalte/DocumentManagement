import { describe, expect, it } from 'vitest';
import { extensionOf, newScanId } from './scan-id';

describe('scan id helpers', () => {
  it('picks an extension from the source uri', () => {
    expect(extensionOf('file:///tmp/a.PNG')).toBe('png');
    expect(extensionOf('file:///tmp/a.webp?x=1')).toBe('webp');
    expect(extensionOf('file:///tmp/a')).toBe('jpg');
  });

  it('creates a non-empty scan id without throwing', () => {
    expect(newScanId().length).toBeGreaterThan(8);
  });
});
