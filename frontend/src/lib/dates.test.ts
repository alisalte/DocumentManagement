import { describe, expect, it } from 'vitest';
import { gregorianToJalali, isJalaliLeapYear, parseJalaliDate, toJalaliInput } from './dates';

describe('Jalali conversion', () => {
  it.each([
    ['1403/01/01', '2024-03-20'],
    ['1402/12/29', '2024-03-19'],
    ['1399/12/30', '2021-03-20'],
    ['1400/01/01', '2021-03-21'],
    ['1403/12/30', '2025-03-20'],
    ['1367/10/11', '1989-01-01'],
    ['۱۴۰۳/۰۵/۱۲', '2024-08-02'],
    ['1403-5-12', '2024-08-02'],
  ])('%s is %s', (jalali, gregorian) => {
    expect(parseJalaliDate(jalali)).toBe(gregorian);
  });

  it('round-trips every day of several years', () => {
    for (let gy = 2019; gy <= 2027; gy++) {
      for (let day = new Date(Date.UTC(gy, 0, 1)); day.getUTCFullYear() === gy; day.setUTCDate(day.getUTCDate() + 1)) {
        const iso = day.toISOString().slice(0, 10);
        expect(parseJalaliDate(toJalaliInput(iso))).toBe(iso);
      }
    }
  });

  it('knows the leap years', () => {
    expect(isJalaliLeapYear(1399)).toBe(true);
    expect(isJalaliLeapYear(1403)).toBe(true);
    expect(isJalaliLeapYear(1402)).toBe(false);
    expect(gregorianToJalali(2024, 3, 20)).toEqual({ jy: 1403, jm: 1, jd: 1 });
  });

  it.each(['1402/12/30', '1403/13/01', '1403/07/31', 'not a date', '1403/1'])('rejects %s', (input) => {
    expect(parseJalaliDate(input)).toBeNull();
  });
});
