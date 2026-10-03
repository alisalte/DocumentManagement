import { describe, expect, it } from 'vitest';
import { vazirFamily } from './fonts';

describe('vazirFamily', () => {
  it('maps the weights used in the interface', () => {
    expect(vazirFamily()).toBe('Vazir');
    expect(vazirFamily('400')).toBe('Vazir');
    expect(vazirFamily('normal')).toBe('Vazir');
    expect(vazirFamily('600')).toBe('Vazir-SemiBold');
    expect(vazirFamily('700')).toBe('Vazir-Bold');
    expect(vazirFamily('bold')).toBe('Vazir-Bold');
    expect(vazirFamily('800')).toBe('Vazir-ExtraBold');
  });
});
