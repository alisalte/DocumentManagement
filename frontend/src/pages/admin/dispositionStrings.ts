/** Persian copy for the disposition / Certificate of Destruction admin screen (phase 10.4). */
export const d = {
  menu: 'امحای اسناد',
  title: 'امحای اسناد پس از پایان نگهداری',
  subtitle: 'اسنادی که دورهٔ نگهداری‌شان تمام شده؛ امحا فقط پس از درخواست، تأیید و تأیید نهایی.',
  empty: 'سندی در صف امحا نیست.',
  recordTitle: 'عنوان',
  status: 'وضعیت',
  retentionExpiry: 'پایان نگهداری',
  legalHold: 'نگهداری قانونی',
  onHold: 'فعال',
  notOnHold: 'ندارد',
  disposition: 'وضعیت درخواست امحا',
  noDisposition: 'بدون درخواست',
  finalVersion: 'نسخه‌ی نهایی',
  request: 'درخواست امحا',
  approve: 'تأیید',
  reject: 'رد',
  destroy: 'امحا',
  viewCertificate: 'گواهی امحا',
  reason: 'دلیل',
  rejectionReason: 'دلیل رد',
  rejectionRequired: 'برای رد، دلیل الزامی است.',
  confirmDestroyTitle: 'تأیید نهایی امحا',
  confirmDestroyBody:
    'امحا برگشت‌ناپذیر است. محتوای فایل علامت حذف می‌گیرد، وضعیت رکورد به «امحاشده» می‌رود و گواهی امحا صادر می‌گردد.',
  confirmDestroyLabel: 'برای ادامه عبارت «امحا» را بنویسید',
  confirmDestroyWord: 'امحا',
  cancel: 'انصراف',
  close: 'بستن',
  submit: 'ثبت',
  certificateTitle: 'گواهی امحا',
  certificateNumber: 'شماره گواهی',
  contentHash: 'هش محتوا (SHA-256)',
  certificateHash: 'هش گواهی',
  approvedBy: 'تأییدکننده',
  approvedAt: 'زمان تأیید',
  destroyedBy: 'امحاکننده',
  destroyedAt: 'زمان امحا',
  legalHoldCheckedAt: 'آخرین بررسی نگهداری قانونی',
  retentionPolicy: 'سیاست نگهداری',
  auditHistory: 'سابقهٔ درخواست امحا',
  none: '—',
  pendingReview: 'در انتظار بررسی',
  approved: 'تأییدشده',
  rejected: 'ردشده',
  destroyed: 'امحاشده',
  pendingDisposal: 'منتظر امحا',
  expired: 'منقضی',
  active: 'فعال',
  requested: 'درخواست امحا ثبت شد.',
  approvedOk: 'درخواست امحا تأیید شد.',
  rejectedOk: 'درخواست امحا رد شد؛ رکورد به وضعیت منقضی بازگشت.',
  destroyedOk: 'سند امحا شد و گواهی صادر گردید.',
  exportJson: 'خروجی JSON',
};

export const recordStatusLabel = (status: string | null | undefined): string => {
  switch (status) {
    case 'PendingDisposal':
      return d.pendingDisposal;
    case 'Destroyed':
      return d.destroyed;
    case 'Expired':
      return d.expired;
    case 'Active':
      return d.active;
    default:
      return status || d.none;
  }
};
