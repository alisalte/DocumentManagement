import { describe, expect, it } from 'vitest';
import type { AuditEntry } from '../../lib/api';
import { describeUserAgent, formatValue, headline, readableFields } from './auditDescribe';

const documentId = '0199f0a0-0000-7000-8000-000000000001';
const categoryId = '0199f0a0-0000-7000-8000-000000000002';
const otherCategoryId = '0199f0a0-0000-7000-8000-000000000003';

const entry = (overrides: Partial<AuditEntry>): AuditEntry => ({
  id: '1',
  occurredAt: '2026-10-20T10:00:00Z',
  action: 'DOCUMENT_UPDATED',
  outcome: 'SUCCESS',
  actorType: 'USER',
  userId: 'u',
  userName: 'علی',
  shareLinkId: null,
  entityType: 'Document',
  entityId: documentId,
  documentId,
  versionId: null,
  ipAddress: null,
  userAgent: null,
  correlationId: null,
  metadata: '{}',
  names: { [documentId]: 'قرارداد اجاره', [categoryId]: 'مالی', [otherCategoryId]: 'حقوقی' },
  ...overrides,
});

describe('audit rows in plain words', () => {
  it('names the document instead of showing its id', () => {
    expect(headline(entry({}))).toEqual({ action: 'به‌روزرسانی سند', object: '«قرارداد اجاره»', version: null });
  });

  it('says which permission, for whom and where', () => {
    const userId = '0199f0a0-0000-7000-8000-000000000009';
    const grant = entry({
      action: 'PERMISSION_GRANTED',
      documentId: null,
      metadata: JSON.stringify({ permission: 'DOCUMENT_VIEW', subjectId: userId, resourceId: categoryId }),
      names: { [userId]: 'سارا احمدی', [categoryId]: 'مالی' },
    });
    expect(headline(grant).object).toBe('«دیدن سند» برای «سارا احمدی» روی «مالی»');
  });

  it('does not repeat the actor as the subject of a login', () => {
    const login = entry({ action: 'LOGIN', userId: 'u1', entityId: 'u1', documentId: null, names: { u1: 'علی' } });
    expect(headline(login).object).toBeNull();
  });

  it('shows a before/after change only for what changed, with ids by name', () => {
    const fields = readableFields(
      entry({
        metadata: JSON.stringify({
          before: { title: 'الف', description: null, categoryId },
          after: { title: 'الف', description: null, categoryId: otherCategoryId },
          moved: false,
        }),
      }),
    );
    expect(fields).toContainEqual({ key: 'categoryId', label: 'پوشه', change: ['مالی', 'حقوقی'] });
    expect(fields.some((field) => field.key === 'title')).toBe(false);
    expect(fields.some((field) => field.key === 'moved')).toBe(false);
  });

  it('turns .NET ticks of a seal check into a date and hides the proof', () => {
    const ticks = 621355968000000000 + Date.UTC(2026, 9, 20) * 10000;
    expect(formatValue('checkedAt', ticks, {})).not.toMatch(/\d{10,}/);
    const fields = readableFields(entry({ action: 'AUDIT_SEALS_VERIFIED', metadata: JSON.stringify({ proof: 'abc', rows: 10128 }) }));
    expect(fields.map((field) => field.key)).toEqual(['rows']);
  });

  it('leaves out ids it cannot name and labels permission codes', () => {
    expect(formatValue('subjectId', '0199f0a0-0000-7000-8000-00000000ffff', {})).toBeNull();
    expect(formatValue('permission', 'DOCUMENT_VIEW', {})).toBe('دیدن سند');
  });

  it('shows a watermark added where there was none', () => {
    const fields = readableFields(entry({ action: 'DOCUMENT_WATERMARK_CHANGED', metadata: JSON.stringify({ before: null, after: 'محرمانه' }) }));
    expect(fields).toEqual([{ key: 'value', label: 'مقدار', change: ['—', 'محرمانه'] }]);
  });

  it('describes the device briefly', () => {
    expect(describeUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0 Safari/537.36')).toBe(
      'Chrome روی Windows',
    );
  });
});
