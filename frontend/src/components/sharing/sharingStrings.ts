import type { SharePermission, ShareState } from '../../lib/api';

export const s = {
  share: 'اشتراک‌گذاری',
  shares: 'اشتراک‌ها',
  sharedWithMe: 'اشتراک‌شده با من',
  sharedWithMeEmpty: 'کسی سندی با شما به اشتراک نگذاشته است.',
  withColleague: 'با همکار',
  externalLink: 'پیوند بیرونی',
  recipient: 'گیرنده',
  version: 'نسخه',
  versionHelp: 'اشتراک فقط همین نسخه را نشان می‌دهد، نه نسخه‌های بعدی را.',
  permissions: 'دسترسی',
  expiry: 'اعتبار',
  noExpiry: 'تا زمان لغو',
  days: (count: number) => `${count.toLocaleString('fa-IR')} روز`,
  message: 'پیام برای گیرنده',
  sharedBy: 'از طرف',
  sharedWith: 'با',
  expires: 'تا',
  shareAction: 'اشتراک‌گذاری',
  createLink: 'ساختن پیوند',
  maxOpenings: 'حداکثر دفعات باز شدن',
  maxOpeningsHelp: 'خالی یعنی بدون محدودیت تا پایان اعتبار.',
  linkPassword: 'گذرواژه‌ی پیوند',
  linkPasswordHelp: 'اختیاری؛ دست‌کم ۶ نویسه. گذرواژه را جدا از پیوند بفرستید.',
  linkLabel: 'یادداشت (فقط برای شما)',
  linkCreated: 'پیوند ساخته شد',
  linkShownOnce: 'این پیوند فقط همین یک بار نمایش داده می‌شود. آن را همین حالا کپی کنید.',
  copy: 'کپی',
  copied: 'کپی شد.',
  close: 'بستن',
  revoke: 'لغو',
  decline: 'حذف از فهرست',
  revokeConfirm: 'دسترسی بلافاصله قطع می‌شود.',
  revoked: 'لغو شد.',
  shared: 'به اشتراک گذاشته شد.',
  openings: 'بار باز شده',
  locked: 'قفل تا',
  password: 'با گذرواژه',
  noShares: 'هنوز اشتراکی نیست.',
  onlyPublished: 'فقط نسخه‌های منتشرشده را می‌توان به اشتراک گذاشت.',
  onlyYours: 'فقط اشتراک‌هایی را می‌بینید که خودتان ساخته‌اید.',
  open: 'باز کردن',

  linkTitle: 'سند اشتراکی',
  linkOpen: 'باز کردن سند',
  linkOpenHelp: 'هر بار باز کردن این پیوند شمرده می‌شود.',
  linkNeedsPassword: 'این پیوند گذرواژه دارد.',
  linkInvalid: 'این پیوند معتبر نیست یا منقضی شده است.',
  linkWrongPassword: 'گذرواژه درست نیست.',
  linkLocked: 'به دلیل تلاش‌های ناموفق، پیوند موقتاً قفل شده است. بعداً دوباره امتحان کنید.',
  linkSessionEnded: 'زمان مشاهده تمام شد. برای ادامه، پیوند را دوباره باز کنید.',
  linkNotIncluded: 'این پیوند شامل این کار نیست.',
  linkWatermark: 'صفحه‌ها با نشان پیوند و زمان مشاهده علامت‌گذاری می‌شوند.',
} as const;

export const permissionLabels: Record<SharePermission, string> = {
  View: 'مشاهده',
  Download: 'دریافت فایل',
  Print: 'چاپ',
};

export const stateLabels: Record<ShareState, string> = {
  Active: 'فعال',
  Expired: 'منقضی',
  Revoked: 'لغوشده',
  UsedUp: 'تمام‌شده',
};

/** Link and share error codes worth their own sentence. */
export function describeShareError(code: string | undefined): string | null {
  switch (code) {
    case 'share.exceeds_rights':
      return 'فقط کارهایی را می‌توانید به اشتراک بگذارید که خودتان اجازه‌اش را دارید.';
    case 'share.version_not_published':
      return s.onlyPublished;
    case 'share.content_blocked':
      return 'تا پایان بررسی ضدبدافزار، این نسخه را نمی‌توان به اشتراک گذاشت.';
    case 'share.recipient_not_found':
      return 'گیرنده کاربر فعالی نیست.';
    case 'share.self':
      return 'نمی‌توانید سند را با خودتان به اشتراک بگذارید.';
    case 'share_link.not_allowed_for_type':
      return 'اسناد این نوع را نمی‌توان بیرون از سازمان به اشتراک گذاشت.';
    case 'share_link.disabled':
      return 'پیوندهای بیرونی در این سامانه غیرفعال است.';
    case 'share_link.password_too_short':
      return 'گذرواژه‌ی پیوند دست‌کم ۶ نویسه باشد.';
    case 'share_link.expiry_too_far':
      return 'اعتبار پیوند بیش از حد مجاز است.';
    case 'share_link.invalid':
      return s.linkInvalid;
    case 'share_link.password_incorrect':
      return s.linkWrongPassword;
    case 'share_link.locked':
      return s.linkLocked;
    case 'share_link.session_expired':
      return s.linkSessionEnded;
    case 'share_link.not_included':
      return s.linkNotIncluded;
    default:
      return null;
  }
}
