import { describe, expect, it } from 'vitest';
import { resolveApiUrl } from './env';

describe('resolveApiUrl', () => {
  it('keeps a same-origin path in a production bundle', () => {
    expect(resolveApiUrl('/api/v1', true)).toBe('/api/v1');
    expect(resolveApiUrl('/api/v1/', true)).toBe('/api/v1');
  });

  it('accepts https and rejects plain http in production', () => {
    expect(resolveApiUrl('https://archive.example/api/v1', true)).toBe('https://archive.example/api/v1');
    expect(() => resolveApiUrl('http://localhost:5080/api/v1', true)).toThrow(/HTTPS/);
  });

  it('allows http while developing', () => {
    expect(resolveApiUrl('http://10.0.2.2:5080/api/v1', false)).toBe('http://10.0.2.2:5080/api/v1');
  });
});
