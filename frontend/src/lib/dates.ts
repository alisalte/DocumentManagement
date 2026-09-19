/**
 * Display-only date formatting.
 *
 * The server stores every timestamp as UTC in a timestamptz column and sends ISO-8601. The Jalali
 * calendar exists purely for display, which is why this lives in the UI and not in persistence
 * (decision D4). Intl handles the conversion, so no calendar library is needed.
 */
const jalaliDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', {
  dateStyle: 'medium',
  timeStyle: 'short',
});

const jalaliDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'medium' });

const gregorianDateTime = new Intl.DateTimeFormat('en-GB', {
  dateStyle: 'medium',
  timeStyle: 'short',
});

export type CalendarLocale = 'fa' | 'en';

export function formatDateTime(isoTimestamp: string, locale: CalendarLocale = 'fa'): string {
  const value = new Date(isoTimestamp);
  if (Number.isNaN(value.getTime())) {
    return '';
  }

  return locale === 'fa' ? jalaliDateTime.format(value) : gregorianDateTime.format(value);
}

export function formatDate(isoTimestamp: string, locale: CalendarLocale = 'fa'): string {
  const value = new Date(isoTimestamp);
  if (Number.isNaN(value.getTime())) {
    return '';
  }

  return locale === 'fa' ? jalaliDate.format(value) : value.toLocaleDateString('en-GB');
}

/** Persian and Arabic digits normalised to ASCII, for search boxes and numeric input. */
export function normalizeDigits(input: string): string {
  const persian = '۰۱۲۳۴۵۶۷۸۹';
  const arabic = '٠١٢٣٤٥٦٧٨٩';

  return input.replace(/[۰-۹٠-٩]/g, (digit) => {
    const index = persian.indexOf(digit);
    return String(index >= 0 ? index : arabic.indexOf(digit));
  });
}
