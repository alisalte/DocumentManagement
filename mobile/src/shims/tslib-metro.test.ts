import { describe, expect, it } from 'vitest';

interface TslibModule {
  __esModule?: boolean;
  default?: TslibModule;
  __extends?: unknown;
  __awaiter?: unknown;
}

describe('metro tslib shim', () => {
  it('gives Metro a default object that still has the named helpers', async () => {
    const loaded = (await import('./tslib-metro.js')) as { default: TslibModule };
    const tslib = loaded.default ?? (loaded as TslibModule);
    const mod = tslib.__esModule ? tslib : { default: tslib };
    expect(typeof mod.default?.__extends).toBe('function');
    expect(typeof mod.default?.__awaiter).toBe('function');
  });
});
