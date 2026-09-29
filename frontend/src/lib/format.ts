const persianNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 });

/** "۲٫۴ مگابایت". Binary units, because that is what file managers show. */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) {
    return '';
  }

  const units = ['بایت', 'کیلوبایت', 'مگابایت', 'گیگابایت'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  return `${persianNumber.format(value)} ${units[unit]}`;
}

export function formatNumber(value: number): string {
  return persianNumber.format(value);
}

/** Version labels stay Latin (V3.2): they are identifiers people read out and type. */
export function versionLabel(label: string | null | undefined): string {
  return label ?? '';
}

/** A fresh key per user action; retries of the same action reuse it (Idempotency-Key). */
export function newIdempotencyKey(): string {
  // randomUUID exists only in secure contexts (HTTPS or localhost). LAN IP over HTTP
  // (e.g. http://192.168.x.x:8090) is not secure, so fall back to getRandomValues.
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
