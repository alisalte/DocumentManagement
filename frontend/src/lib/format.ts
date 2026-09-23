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
  return crypto.randomUUID();
}
