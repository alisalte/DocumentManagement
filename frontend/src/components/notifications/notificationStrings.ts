import type { AppNotification } from '../../lib/api';

export const n = {
  notifications: 'اعلان‌ها',
  empty: 'اعلان تازه‌ای ندارید.',
  markAllRead: 'همه خوانده شد',
  more: 'قدیمی‌ترها',
  unread: (count: number) => `${count.toLocaleString('fa-IR')} خوانده‌نشده`,
};

const outcomes: Record<string, string> = {
  Approved: 'تأیید شد',
  Rejected: 'رد شد',
  ChangesRequested: 'نیازمند اصلاح است',
  Cancelled: 'لغو شد',
};

const quoted = (title: string | undefined, version: string | undefined) =>
  `«${title ?? '—'}»${version ? ` (${version})` : ''}`;

/** One line of text for a notification. The server stores facts; the wording lives here. */
export function describeNotification(item: AppNotification): string {
  const { payload } = item;
  const document = quoted(payload.documentTitle, payload.versionLabel);
  const actor = item.actorName ?? 'کسی';

  switch (item.type) {
    case 'TASK_ASSIGNED':
      return payload.stepName
        ? `${document} در مرحله‌ی «${payload.stepName}» منتظر بررسی شماست.`
        : `${document} منتظر بررسی شماست.`;
    case 'TASK_OVERDUE':
      return `مهلت بررسی ${document} گذشته است.`;
    case 'WORKFLOW_FINISHED':
      return `بررسی ${document} به پایان رسید: ${outcomes[payload.outcome ?? ''] ?? payload.outcome ?? ''}.`;
    case 'DOCUMENT_SHARED':
      return `${actor} ${document} را با شما به اشتراک گذاشت.`;
    default:
      return document;
  }
}

/** Where a notification leads. Access is checked there as usual. */
export function notificationTarget(item: AppNotification): string | null {
  switch (item.type) {
    case 'TASK_ASSIGNED':
    case 'TASK_OVERDUE':
      return '/tasks';
    case 'DOCUMENT_SHARED':
      return item.documentId && item.versionId ? `/shared/${item.documentId}/${item.versionId}` : '/shared';
    default:
      return item.documentId ? `/documents/${item.documentId}` : null;
  }
}
