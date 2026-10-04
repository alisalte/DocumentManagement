import axios from 'axios';
import type { ProblemDetails } from '../types/api';
import { isDevLogEnabled } from '../config/env';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const codeMessages: Record<string, string> = {
  'auth.invalid_credentials': 'نام کاربری یا رمز عبور نادرست است.',
  'auth.account_locked': 'حساب به‌طور موقت قفل شده است. کمی بعد دوباره تلاش کنید.',
  'auth.invalid_refresh_token': 'نشست شما منقضی شده است.',
  'auth.unauthenticated': 'نشست شما منقضی شده است.',
  'auth.password_change_required': 'پیش از ادامه باید رمز عبور را تغییر دهید.',
  'password.too_short': 'رمز عبور باید حداقل ۱۲ نویسه باشد.',
  'document.duplicate_file': 'این فایل قبلاً به‌عنوان سندی که می‌توانید ببینید ثبت شده است.',
  'document.title_required': 'عنوان سند الزامی است.',
  'upload.not_found': 'فایل آپلودشده پیدا نشد یا قبلاً استفاده شده است.',
  'upload.extension_not_allowed': 'این نوع سند این فرمت فایل را نمی‌پذیرد.',
  'upload.too_large_for_type': 'حجم فایل برای این نوع سند بیش از حد مجاز است.',
  'category.inactive': 'در این پوشه نمی‌توان سند ثبت کرد.',
  'category.not_found': 'پوشه پیدا نشد.',
  'document_type.not_found': 'نوع سند پیدا نشد.',
  network: 'اتصال به سرور برقرار نشد.',
  config: 'آدرس سرور تنظیم نشده است. آن را در صفحه ورود وارد کنید.',
};

const statusMessages: Record<number, string> = {
  400: 'اطلاعات واردشده معتبر نیست.',
  401: 'نشست شما منقضی شده است.',
  403: 'شما دسترسی لازم را ندارید.',
  404: 'اطلاعات موردنظر پیدا نشد.',
  409: 'این عملیات با وضعیت فعلی سازگار نیست.',
  422: 'اطلاعات واردشده معتبر نیست.',
  429: 'درخواست‌های زیادی ارسال شده است. کمی بعد دوباره تلاش کنید.',
  500: 'خطایی در سرور رخ داده است.',
};

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (axios.isAxiosError(error)) {
    if (!error.response) {
      return new ApiError(0, 'network', 'network');
    }
    const problem = (error.response.data ?? {}) as ProblemDetails;
    const apiError = new ApiError(
      error.response.status,
      problem.code,
      problem.detail ?? problem.title ?? 'request failed',
      problem.errors ?? {},
    );
    if (isDevLogEnabled()) {
      const path = error.config?.url ?? '';
      console.warn(`[api] ${apiError.status} ${apiError.code ?? ''} ${path}`);
    }
    return apiError;
  }
  if (error instanceof Error && error.message.includes('EXPO_PUBLIC_API_URL')) {
    return new ApiError(0, 'config', error.message);
  }
  return new ApiError(0, 'unknown', 'unexpected');
}

/** Persian text for the screen. Technical detail stays in the logs. */
export function userMessage(error: unknown): string {
  if (error instanceof Error && !(error instanceof ApiError) && !axios.isAxiosError(error)) {
    // Local validation / prepare failures already carry Persian text.
    if (/[\u0600-\u06FF]/.test(error.message)) return error.message;
  }
  const apiError = error instanceof ApiError ? error : toApiError(error);
  if (apiError.code && codeMessages[apiError.code]) return codeMessages[apiError.code];
  if (apiError.status === 0) return codeMessages.network;
  return statusMessages[apiError.status] ?? 'خطای غیرمنتظره رخ داد.';
}

/** Credential and lockout failures are not expired sessions. */
export function shouldRefreshAfterUnauthorized(code: string | undefined): boolean {
  return code !== 'auth.invalid_credentials' && code !== 'auth.account_locked' && code !== 'auth.invalid_refresh_token';
}
