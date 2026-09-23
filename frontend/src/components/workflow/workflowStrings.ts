import type { AssigneeType, WorkflowAction, WorkflowInstance } from '../../lib/api';

export const w = {
  inbox: 'کارتابل',
  workflows: 'گردش‌های کار',
  inboxEmpty: 'کاری در انتظار شما نیست.',
  workflow: 'گردش کار',
  noWorkflow: 'این سند هنوز وارد گردش کار نشده است.',
  start: 'ارسال برای بررسی',
  startHelp: 'نسخه‌ی جاری برای تأیید فرستاده می‌شود.',
  cancel: 'لغو گردش کار',
  cancelReason: 'دلیل لغو',
  close: 'بستن',
  comment: 'توضیح',
  commentRequired: 'توضیح برای این اقدام لازم است.',
  forwardTo: 'ارجاع به',
  returnTo: 'بازگشت به مرحله',
  returnDefault: 'مرحله‌ی قبل',
  due: 'مهلت',
  overdue: 'دیرکرد',
  waitingFor: 'در انتظار',
  step: 'مرحله',
  version: 'نسخه',
  skipped: 'مراحل ردشده',
  attention: 'نیاز به رسیدگی مدیر',
  sent: 'انجام شد.',
  started: 'برای بررسی فرستاده شد.',
  cancelled: 'گردش کار لغو شد.',
  group: 'گروه',
  role: 'نقش',
  open: 'باز کردن سند',
} as const;

export const actionLabels: Record<WorkflowAction, string> = {
  Approve: 'تأیید',
  Reject: 'رد',
  Return: 'بازگرداندن',
  RequestChanges: 'درخواست اصلاح',
  Forward: 'ارجاع',
};

export const statusLabels: Record<WorkflowInstance['status'], string> = {
  Running: 'در جریان',
  Approved: 'تأییدشده',
  Rejected: 'ردشده',
  ChangesRequested: 'نیازمند اصلاح',
  Cancelled: 'لغوشده',
};

export const statusColors: Record<WorkflowInstance['status'], 'info' | 'success' | 'error' | 'warning' | 'default'> = {
  Running: 'info',
  Approved: 'success',
  Rejected: 'error',
  ChangesRequested: 'warning',
  Cancelled: 'default',
};

export const approvalLabels: Record<string, string> = {
  NotRequired: 'بدون گردش کار',
  Draft: 'پیش‌نویس',
  InWorkflow: 'در حال بررسی',
  Approved: 'تأییدشده',
  Rejected: 'ردشده',
  ChangesRequested: 'نیازمند اصلاح',
  Cancelled: 'لغوشده',
};

export const assigneeTypeLabels: Record<AssigneeType, string> = {
  User: 'کاربر مشخص',
  Group: 'گروه',
  Role: 'نقش',
  DocumentOwner: 'مالک سند',
  Creator: 'ایجادکننده‌ی نسخه',
  Manager: 'مدیر ایجادکننده',
  DynamicUserField: 'کاربرِ فیلد سند',
};
