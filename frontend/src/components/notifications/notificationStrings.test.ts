import { describe, expect, it } from 'vitest';
import type { AppNotification } from '../../lib/api';
import { describeNotification, notificationTarget } from './notificationStrings';

const base: AppNotification = {
  id: 'n1',
  type: 'TASK_ASSIGNED',
  actorId: 'u1',
  actorName: 'سارا احمدی',
  documentId: 'd1',
  versionId: 'v1',
  payload: { documentTitle: 'قرارداد', versionLabel: 'V2.1', stepName: 'حقوقی' },
  createdAt: '2026-09-24T10:00:00Z',
  readAt: null,
};

describe('notifications', () => {
  it('words each type and points where it leads', () => {
    expect(describeNotification(base)).toBe('«قرارداد» (V2.1) در مرحله‌ی «حقوقی» منتظر بررسی شماست.');
    expect(notificationTarget(base)).toBe('/tasks');

    const shared = { ...base, type: 'DOCUMENT_SHARED' };
    expect(describeNotification(shared)).toBe('سارا احمدی «قرارداد» (V2.1) را با شما به اشتراک گذاشت.');
    expect(notificationTarget(shared)).toBe('/shared/d1/v1');

    const finished = { ...base, type: 'WORKFLOW_FINISHED', payload: { ...base.payload, outcome: 'Rejected' as const } };
    expect(describeNotification(finished)).toBe('بررسی «قرارداد» (V2.1) به پایان رسید: رد شد.');
    expect(notificationTarget(finished)).toBe('/documents/d1');
  });

  it('survives a type it does not know', () => {
    expect(describeNotification({ ...base, type: 'SOMETHING_NEW' })).toBe('«قرارداد» (V2.1)');
  });
});
