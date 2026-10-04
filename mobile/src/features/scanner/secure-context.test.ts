import { describe, expect, it } from 'vitest';
import { cameraHttpsHint, isInsecureBrowserContext } from './secure-context';

describe('secure context helpers', () => {
  it('treats missing window as secure enough for native', () => {
    expect(isInsecureBrowserContext()).toBe(false);
  });

  it('flags an insecure browser context', () => {
    expect(isInsecureBrowserContext(false)).toBe(true);
    expect(isInsecureBrowserContext(true)).toBe(false);
  });

  it('builds a https hint from an http url', () => {
    expect(cameraHttpsHint('http://62.60.166.71:8091/')).toContain('https://62.60.166.71:8091/');
  });
});
