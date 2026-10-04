import { describe, expect, it } from 'vitest';
import { ApiError, userMessage } from './errors';

describe('userMessage', () => {
  it('maps common HTTP failures to Persian', () => {
    expect(userMessage(new ApiError(401, undefined, 'raw'))).toBe('نشست شما منقضی شده است.');
    expect(userMessage(new ApiError(403, undefined, 'raw'))).toBe('شما دسترسی لازم را ندارید.');
    expect(userMessage(new ApiError(404, undefined, 'raw'))).toBe('اطلاعات موردنظر پیدا نشد.');
    expect(userMessage(new ApiError(409, undefined, 'raw'))).toBe('این عملیات با وضعیت فعلی سازگار نیست.');
    expect(userMessage(new ApiError(422, undefined, 'raw'))).toBe('اطلاعات واردشده معتبر نیست.');
    expect(userMessage(new ApiError(429, undefined, 'raw'))).toBe('درخواست‌های زیادی ارسال شده است. کمی بعد دوباره تلاش کنید.');
    expect(userMessage(new ApiError(500, undefined, 'raw'))).toBe('خطایی در سرور رخ داده است.');
    expect(userMessage(new ApiError(0, 'network', 'network'))).toContain('اتصال به سرور برقرار نشد');
  });
});
