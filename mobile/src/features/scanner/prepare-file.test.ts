import { describe, expect, it } from 'vitest';
import { prepareScanFile } from './prepare-file';
import type { ScanPage } from './scanner.types';

const one: ScanPage[] = [{ id: 'p1', uri: 'file:///cache/scan-1.jpg', width: 100, height: 200 }];
const many: ScanPage[] = [
  one[0]!,
  { id: 'p2', uri: 'file:///cache/scan-2.png', width: 100, height: 200 },
];

describe('prepareScanFile', () => {
  it('returns the captured image for image format', async () => {
    const file = await prepareScanFile(one, 'image');
    expect(file).toEqual({
      uri: 'file:///cache/scan-1.jpg',
      fileName: 'scan.jpg',
      mimeType: 'image/jpeg',
    });
  });

  it('rejects image format for multiple pages', async () => {
    await expect(prepareScanFile(many, 'image')).rejects.toThrow(/یک صفحه/);
  });
});
