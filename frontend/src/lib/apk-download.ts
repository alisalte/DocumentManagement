export const SCANNER_APK_PATH = '/downloads/dms-scanner.apk';

export function scannerDownloadUrl(origin: string): string {
  return `${origin.replace(/\/$/, '')}${SCANNER_APK_PATH}`;
}

/** A phone cannot download an APK from the computer's loopback address. */
export function downloadHostIsLocal(hostname: string): boolean {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';
}
