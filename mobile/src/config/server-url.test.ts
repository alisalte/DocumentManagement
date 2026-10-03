import { describe, expect, it } from 'vitest';
import { normalizeServerUrl } from './server-url';

describe('normalizeServerUrl', () => {
  it('appends the API prefix to an origin', () => {
    expect(normalizeServerUrl('http://192.168.1.10:8090')).toBe('http://192.168.1.10:8090/api/v1');
    expect(normalizeServerUrl('http://192.168.1.10:8090/')).toBe('http://192.168.1.10:8090/api/v1');
    expect(normalizeServerUrl('192.168.1.10:8090')).toBe('http://192.168.1.10:8090/api/v1');
  });

  it('keeps a full API URL and its scheme', () => {
    expect(normalizeServerUrl('https://archive.example/api/v1')).toBe('https://archive.example/api/v1');
    expect(normalizeServerUrl('http://10.0.0.8:8090/api/v1/')).toBe('http://10.0.0.8:8090/api/v1');
  });

  it('rejects an empty or unexpected address', () => {
    expect(() => normalizeServerUrl('   ')).toThrow(/empty/);
    expect(() => normalizeServerUrl('http://192.168.1.10:8090/login')).toThrow(/invalid/);
    expect(() => normalizeServerUrl('ftp://192.168.1.10')).toThrow(/invalid/);
  });
});
