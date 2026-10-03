const persianDigits = '۰۱۲۳۴۵۶۷۸۹';

export function faDigits(value: string | number): string {
  return String(value).replace(/\d/g, (digit) => persianDigits[Number(digit)] ?? digit);
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function formatDateFa(iso: string | null | undefined): string {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return faDigits(iso.slice(0, 10));
  return faDigits(date.toLocaleDateString('fa-IR'));
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '—';
  if (bytes < 1024) return `${faDigits(bytes)} B`;
  if (bytes < 1024 * 1024) return `${faDigits((bytes / 1024).toFixed(1))} KB`;
  return `${faDigits((bytes / (1024 * 1024)).toFixed(1))} MB`;
}
