import { describe, expect, it } from 'vitest';
import { downloadHostIsLocal, scannerDownloadUrl } from './apk-download';

describe('scanner download url', () => {
  it('points the QR code at the apk on this origin', () => {
    expect(scannerDownloadUrl('http://192.168.1.10:8090')).toBe(
      'http://192.168.1.10:8090/downloads/dms-scanner.apk',
    );
    expect(scannerDownloadUrl('http://192.168.1.10:8090/')).toBe(
      'http://192.168.1.10:8090/downloads/dms-scanner.apk',
    );
  });

  it('treats loopback as unreachable from a phone', () => {
    expect(downloadHostIsLocal('localhost')).toBe(true);
    expect(downloadHostIsLocal('127.0.0.1')).toBe(true);
    expect(downloadHostIsLocal('192.168.1.10')).toBe(false);
  });
});
