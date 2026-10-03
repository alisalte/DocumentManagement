const persianDigits = '۰۱۲۳۴۵۶۷۸۹';

export function faDigits(value: string | number): string {
  return String(value).replace(/\d/g, (digit) => persianDigits[Number(digit)] ?? digit);
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
