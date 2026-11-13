/** Persian copy for the legacy import admin screen (phase 10.6). */
export const i = {
  menu: 'ورود از سامانهٔ قدیمی',
  title: 'ورود اسناد از سامانهٔ قدیمی',
  subtitle: 'با مانیفست JSON فایل‌ها را اعتبارسنجی کنید، وارد کنید و گزارش بگیرید — بدون دور زدن دسترسی‌ها.',
  create: 'ایجاد کار ورود',
  name: 'نام کار',
  sourceSystem: 'سامانهٔ مبدأ',
  manifest: 'مانیفست JSON',
  filesRootHint: 'ریشهٔ فایل‌ها فقط روی سرور (Dms:Import:FilesRoot) تنظیم می‌شود؛ اینجا زیرپوشه نسبی اختیاری است.',
  relativeRoot: 'زیرپوشه نسبی (اختیاری)',
  failurePolicy: 'سیاست خطا',
  continueOnError: 'ادامه با خطا',
  stopOnError: 'توقف با خطا',
  createCategories: 'ایجاد پوشه‌های مفقود',
  dryRunOnly: 'فقط اعتبارسنجی (بدون ورود)',
  validate: 'اعتبارسنجی',
  start: 'شروع ورود',
  pause: 'توقف موقت',
  resume: 'ازسرگیری',
  retry: 'تلاش مجدد خطاها',
  report: 'گزارش',
  refresh: 'بازخوانی',
  jobs: 'کارهای ورود',
  empty: 'هنوز کار ورودی ثبت نشده است.',
  status: 'وضعیت',
  progress: 'پیشرفت',
  processed: 'پردازش‌شده',
  succeeded: 'موفق',
  failed: 'ناموفق',
  skipped: 'ردشده',
  invalid: 'نامعتبر',
  bytes: 'بایت',
  items: 'آیتم‌ها',
  filterStatus: 'فیلتر وضعیت',
  all: 'همه',
  sourceId: 'شناسه مبدأ',
  path: 'مسیر',
  error: 'خطا',
  validationTitle: 'گزارش اعتبارسنجی',
  warnings: 'هشدار',
  confirmStart: 'شروع ورود، اسناد واقعی ایجاد می‌کند. ادامه می‌دهید؟',
  cancel: 'انصراف',
  submit: 'ثبت',
  close: 'بستن',
  downloadReport: 'دانلود JSON گزارش',
  selectJob: 'یک کار را از فهرست انتخاب کنید.',
};

export const jobStatusLabels: Record<string, string> = {
  Created: 'ایجادشده',
  Validating: 'در حال اعتبارسنجی',
  Ready: 'آماده',
  ValidationFailed: 'اعتبارسنجی ناموفق',
  Running: 'در حال اجرا',
  Paused: 'متوقف موقت',
  Completed: 'تمام‌شده',
  CompletedWithErrors: 'تمام‌شده با خطا',
  Failed: 'ناموفق',
};

export const itemStatusLabels: Record<string, string> = {
  Pending: 'در انتظار',
  Valid: 'معتبر',
  Invalid: 'نامعتبر',
  Ready: 'آماده',
  Running: 'در حال اجرا',
  Succeeded: 'موفق',
  Failed: 'ناموفق',
  Skipped: 'ردشده',
  Retryable: 'قابل تلاش مجدد',
};

export const jobStatusLabel = (status: string) => jobStatusLabels[status] ?? status;
export const itemStatusLabel = (status: string) => itemStatusLabels[status] ?? status;

/** Copying the old archive straight from its SQL Server. */
export const legacy = {
  title: 'ورود مستقیم از SQL Server برنامهٔ قدیمی',
  subtitle:
    'به دیتابیس برنامهٔ قدیمی (ArchiveSaman) وصل شوید؛ همهٔ فایل‌ها یکی‌یکی خوانده و در پوشهٔ واردات سرور ذخیره می‌شوند و در پایان یک کار ورود آماده ساخته می‌شود.',
  connect: 'اتصال به SQL Server قدیمی',
  server: 'آدرس سرور',
  serverHint: 'مثل 192.168.1.10 یا SERVER\\SQLEXPRESS یا 192.168.1.10,1433',
  database: 'نام دیتابیس',
  user: 'نام کاربری SQL',
  userHint: 'یک کاربر SQL با دسترسی فقط-خواندنی. ورود ویندوزی از داخل سرور لینوکسی کار نمی‌کند.',
  password: 'رمز عبور',
  passwordHint: 'رمز ذخیره نمی‌شود و فقط تا پایان همین انتقال در حافظهٔ سرور می‌ماند.',
  encrypt: 'اتصال رمزنگاری‌شده (Encrypt)',
  encryptHint: 'برای SQL Serverهای قدیمی (۲۰۰۸ و ۲۰۱۲) اگر خطای رمزنگاری گرفتید، این گزینه را خاموش کنید.',
  trust: 'اعتماد به گواهی سرور (TrustServerCertificate)',
  name: 'نام (اختیاری)',
  test: 'تست اتصال',
  testing: 'در حال تست…',
  start: 'شروع انتقال همهٔ فایل‌ها',
  confirmStart: (files: string, size: string) =>
    `${files} فایل (${size}) از سرور قدیمی خوانده و در پوشهٔ واردات ذخیره می‌شود. ادامه می‌دهید؟`,
  probeOk: (files: string, size: string, version: string) =>
    `اتصال برقرار شد. ${files} فایل به حجم ${size} پیدا شد. (SQL Server ${version})`,
  tableMissing: 'وجود ندارد',
  copies: 'انتقال‌ها',
  empty: 'هنوز انتقالی انجام نشده است.',
  status: 'وضعیت',
  progress: 'پیشرفت',
  stop: 'توقف',
  confirmStop: 'انتقال متوقف شود؟ فایل‌های منتقل‌شده حفظ می‌شوند و بعداً می‌توانید ادامه دهید.',
  resume: 'ادامه',
  resumeTitle: 'ادامهٔ انتقال',
  resumeHint: 'فایل‌هایی که قبلاً کامل منتقل شده‌اند دوباره خوانده نمی‌شوند.',
  openJob: 'رفتن به کار ورود',
  nextStep: 'انتقال تمام شد. کار ورود ساخته شد؛ آن را انتخاب کنید، «اعتبارسنجی» و سپس «شروع ورود» را بزنید.',
  reused: 'از قبل موجود',
  emptyRows: 'ردیف بدون فایل',
  warnings: 'هشدارها',
  interrupted: 'قطع‌شده (سرور ری‌استارت شد؟)',
  started: 'انتقال شروع شد',
  stopped: 'درخواست توقف ثبت شد',
  resumed: 'انتقال ادامه پیدا کرد',
};

export const legacyStatusLabels: Record<string, string> = {
  Queued: 'در صف',
  Running: 'در حال انتقال',
  Completed: 'تمام‌شده',
  Failed: 'ناموفق',
  Cancelled: 'متوقف‌شده',
};

/** SQL Server errors, by the code the API returns (also at the start of a failed copy's error). */
export const legacyErrors: Record<string, string> = {
  'legacy_sql.login_failed':
    'ورود ناموفق بود: نام کاربری یا رمز اشتباه است، یا ورود با کاربر SQL (SQL Server Authentication) روی سرور فعال نیست.',
  'legacy_sql.database': 'دیتابیس با این نام پیدا نشد یا این کاربر اجازهٔ باز کردن آن را ندارد.',
  'legacy_sql.permission': 'این کاربر اجازهٔ خواندن (SELECT) جدول‌های بایگانی را ندارد.',
  'legacy_sql.timeout': 'سرور در زمان مقرر پاسخ نداد.',
  'legacy_sql.unreachable':
    'به SQL Server دسترسی نیست. آدرس و نام instance را بررسی کنید، TCP/IP در SQL Server Configuration Manager فعال باشد و پورت 1433 (یا SQL Browser روی UDP 1434) در فایروال باز باشد.',
  'legacy_sql.tls': 'اتصال رمزنگاری‌شده برقرار نشد. گزینهٔ «اتصال رمزنگاری‌شده» را خاموش کنید و دوباره امتحان کنید.',
  'legacy_sql.not_archive':
    'اتصال برقرار شد اما جدول‌های فایل برنامهٔ قدیمی (tbl_FileSanad و …) در این دیتابیس نیست. نام دیتابیس را بررسی کنید.',
  'legacy_sql.disabled': 'اتصال به SQL Server قدیمی روی این سرور غیرفعال است (Dms:Import:LegacySql:Enabled).',
  'legacy_sql.server_not_allowed': 'این سرور در فهرست سرورهای مجاز (Dms:Import:LegacySql:AllowedServers) نیست.',
  'legacy_sql.invalid_state': 'این انتقال در وضعیتی نیست که این کار ممکن باشد.',
  'import.files_root': 'پوشهٔ واردات روی سرور تنظیم نشده است (Dms:Import:FilesRoot).',
};

export const legacyStatusLabel = (status: string) => legacyStatusLabels[status] ?? status;

/** A copy's lastError starts with the code ("legacy_sql.login_failed: …"). */
export function describeLegacyError(text: string | null | undefined): string | null {
  if (!text) return null;
  const code = text.split(':', 1)[0].trim();
  return legacyErrors[code] ?? text;
}
