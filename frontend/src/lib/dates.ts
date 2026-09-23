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

/*
 * Jalali <-> Gregorian conversion for date inputs. Intl can format a Gregorian date as Jalali but
 * cannot parse Jalali back, and a date field has to accept "1403/01/01" as typed. This is the
 * standard 33-year-cycle arithmetic (Borkowski), valid for Jalali years -61 to 3177.
 */
const breaks = [
  -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178,
];

const div = (a: number, b: number) => Math.trunc(a / b);
const mod = (a: number, b: number) => a - Math.trunc(a / b) * b;

function jalaliCalendar(jy: number) {
  const gy = jy + 621;
  let leapJ = -14;
  let jp = breaks[0];
  let jump = 0;

  if (jy < jp || jy >= breaks[breaks.length - 1]) {
    throw new RangeError(`Jalali year ${jy} is out of range`);
  }

  for (let i = 1; i < breaks.length; i++) {
    const jm = breaks[i];
    jump = jm - jp;
    if (jy < jm) break;
    leapJ += div(jump, 33) * 8 + div(mod(jump, 33), 4);
    jp = jm;
  }

  let n = jy - jp;
  leapJ += div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
  if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;

  const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
  const march = 20 + leapJ - leapG;

  if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
  let leap = mod(mod(n + 1, 33) - 1, 4);
  if (leap === -1) leap = 4;

  return { leap, gy, march };
}

function gregorianToDayNumber(gy: number, gm: number, gd: number): number {
  let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
  d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
  return d;
}

function dayNumberToGregorian(jdn: number) {
  let j = 4 * jdn + 139361631;
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
  const i = div(mod(j, 1461), 4) * 5 + 308;
  const gd = div(mod(i, 153), 5) + 1;
  const gm = mod(div(i, 153), 12) + 1;
  const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
  return { gy, gm, gd };
}

export function isJalaliLeapYear(jy: number): boolean {
  return jalaliCalendar(jy).leap === 0;
}

export function jalaliMonthLength(jy: number, jm: number): number {
  if (jm <= 6) return 31;
  if (jm <= 11) return 30;
  return isJalaliLeapYear(jy) ? 30 : 29;
}

export function jalaliToGregorian(jy: number, jm: number, jd: number) {
  const { gy, march } = jalaliCalendar(jy);
  const dayNumber = gregorianToDayNumber(gy, 3, march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
  return dayNumberToGregorian(dayNumber);
}

export function gregorianToJalali(gy: number, gm: number, gd: number) {
  const dayNumber = gregorianToDayNumber(gy, gm, gd);
  let jy = dayNumberToGregorian(dayNumber).gy - 621;
  const calendar = jalaliCalendar(jy);
  let k = dayNumber - gregorianToDayNumber(calendar.gy, 3, calendar.march);

  if (k >= 0) {
    if (k <= 185) return { jy, jm: 1 + div(k, 31), jd: mod(k, 31) + 1 };
    k -= 186;
  } else {
    jy -= 1;
    k += 179;
    if (calendar.leap === 1) k += 1;
  }

  return { jy, jm: 7 + div(k, 30), jd: mod(k, 30) + 1 };
}

const pad = (value: number, width = 2) => String(value).padStart(width, '0');

/** "1403/01/01" (Persian or Latin digits, / or - separators) to "2024-03-20", or null. */
export function parseJalaliDate(input: string): string | null {
  const match = /^\s*(\d{4})[/-](\d{1,2})[/-](\d{1,2})\s*$/.exec(normalizeDigits(input));
  if (!match) return null;

  const [jy, jm, jd] = match.slice(1).map(Number);
  if (jm < 1 || jm > 12 || jd < 1 || jy < 1 || jy > 3177 || jd > jalaliMonthLength(jy, jm)) return null;

  const { gy, gm, gd } = jalaliToGregorian(jy, jm, jd);
  return `${pad(gy, 4)}-${pad(gm)}-${pad(gd)}`;
}

/** "2024-03-20" to "1403/01/01" (Latin digits, for editing), or "" when it is not a date. */
export function toJalaliInput(isoDate: string | null | undefined): string {
  const match = isoDate ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoDate) : null;
  if (!match) return '';
  const { jy, jm, jd } = gregorianToJalali(Number(match[1]), Number(match[2]), Number(match[3]));
  return `${jy}/${pad(jm)}/${pad(jd)}`;
}
