/**
 * Every Persian UI string in one place, so wording stays consistent and a second language later
 * is a new table rather than a hunt through components.
 */
export const t = {
  appTitle: 'بایگانی اسناد',
  username: 'نام کاربری',
  password: 'گذرواژه',
  signIn: 'ورود',
  signOut: 'خروج',
  mustChangePassword: 'باید گذرواژه‌ی خود را تغییر دهید.',

  browse: 'اسناد',
  newDocument: 'سند جدید',
  recycleBin: 'سطل بازیافت',
  categories: 'پوشه‌ها',
  allDocuments: 'همه‌ی اسناد',
  includeSubcategories: 'با زیرپوشه‌ها',
  search: 'جستجو در عنوان',
  noDocuments: 'سندی برای نمایش نیست.',
  loading: 'در حال بارگذاری…',
  retry: 'تلاش دوباره',

  title: 'عنوان',
  description: 'توضیحات',
  category: 'پوشه',
  documentType: 'نوع سند',
  tags: 'برچسب‌ها',
  tagsHelp: 'برای افزودن برچسب Enter بزنید.',
  file: 'فایل',
  chooseFile: 'انتخاب فایل',
  changeDescription: 'شرح تغییر',
  version: 'نسخه',
  versions: 'تاریخچه‌ی نسخه‌ها',
  current: 'جاری',
  effective: 'منتشرشده',
  size: 'حجم',
  updatedAt: 'آخرین تغییر',
  createdAt: 'ایجاد',
  uploadedBy: 'بارگذاری',
  sha256: 'اثر انگشت SHA-256',
  download: 'دریافت',
  downloadVersion: 'دریافت این نسخه',
  addVersion: 'افزودن نسخه‌ی جدید',
  save: 'ذخیره',
  cancel: 'انصراف',
  edit: 'ویرایش',
  delete: 'حذف',
  deleteReason: 'دلیل حذف',
  deleteConfirm: 'سند به سطل بازیافت منتقل می‌شود و قابل بازگردانی است.',
  restore: 'بازگردانی',
  deletedAt: 'زمان حذف',
  submit: 'ثبت سند',
  uploading: 'در حال بارگذاری فایل',
  saving: 'در حال ثبت…',
  duplicateNotice: 'این فایل پیش‌تر در این سندها ثبت شده است:',
  notFound: 'سند پیدا نشد یا اجازه‌ی دیدن آن را ندارید.',
  forbidden: 'اجازه‌ی این کار را ندارید.',
  staleVersion: 'پس از باز کردن این صفحه، نسخه‌ی تازه‌تری ثبت شده است. صفحه را تازه کنید.',
  noCreatableCategory: 'در هیچ پوشه‌ای اجازه‌ی ثبت سند ندارید. با مدیر سامانه تماس بگیرید.',
  scanPending: 'در انتظار بررسی ضدبدافزار',
  scanInfected: 'قرنطینه‌شده',
  scanFailed: 'بررسی ناموفق',
  created: 'سند ثبت شد.',
  versionAdded: 'نسخه‌ی جدید ثبت شد.',
  saved: 'ذخیره شد.',
  deleted: 'سند به سطل بازیافت رفت.',
  restored: 'سند بازگردانی شد.',
  previous: 'قبلی',
  next: 'بعدی',
  of: 'از',
  menu: 'پوشه‌ها',
  back: 'بازگشت',
} as const;

/** Server error codes worth a friendlier sentence than the English problem detail. */
export function describeError(error: unknown): string {
  if (error && typeof error === 'object' && 'status' in error) {
    const { status, code, message } = error as { status: number; code?: string; message: string };
    if (code === 'version.stale') return t.staleVersion;
    if (status === 404) return t.notFound;
    if (status === 403) return t.forbidden;
    if (status === 0) return 'ارتباط با سرور برقرار نشد.';
    return message;
  }

  return String(error);
}
