import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as Sharing from 'expo-sharing';

vi.mock('react-native', () => ({
  Platform: { OS: 'android' },
}));

vi.mock('expo-sharing', () => ({
  isAvailableAsync: vi.fn(),
  shareAsync: vi.fn(),
}));

import { savePreparedFile } from './save-file';

describe('savePreparedFile on native', () => {
  beforeEach(() => {
    vi.mocked(Sharing.isAvailableAsync).mockReset();
    vi.mocked(Sharing.shareAsync).mockReset();
    vi.mocked(Sharing.isAvailableAsync).mockResolvedValue(true);
    vi.mocked(Sharing.shareAsync).mockResolvedValue(undefined);
  });

  it('shares through expo-sharing', async () => {
    await savePreparedFile({
      uri: 'file:///cache/scan.pdf',
      fileName: 'scan.pdf',
      mimeType: 'application/pdf',
    });
    expect(Sharing.isAvailableAsync).toHaveBeenCalled();
    expect(Sharing.shareAsync).toHaveBeenCalledWith(
      'file:///cache/scan.pdf',
      expect.objectContaining({ mimeType: 'application/pdf' }),
    );
  });

  it('throws a Persian error when sharing is unavailable', async () => {
    vi.mocked(Sharing.isAvailableAsync).mockResolvedValue(false);
    await expect(
      savePreparedFile({
        uri: 'file:///cache/scan.pdf',
        fileName: 'scan.pdf',
        mimeType: 'application/pdf',
      }),
    ).rejects.toThrow(/اشتراک/);
  });
});
