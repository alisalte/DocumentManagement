import type { AuditEntry } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { permissionLabel, permissionLabels, reasonLabels } from './directoryStrings';
import { a, actionLabel, entityTypeLabel } from './auditStrings';

/**
 * Turns an audit row into something a person can read: a headline («مشاهدهٔ سند «قرارداد»»), and
 * the metadata as labelled, formatted fields. Ids become names where the server could name them;
 * what stays technical (hashes, storage ids, request ids) goes to the technical section only.
 */

/** Persian names for metadata keys; keys not listed are shown only in the technical section. */
const fieldLabels: Record<string, string> = {
  title: 'عنوان',
  documentTitle: 'سند',
  name: 'نام',
  description: 'توضیحات',
  reason: 'دلیل',
  comment: 'توضیح',
  categoryId: 'پوشه',
  parentId: 'پوشه‌ی بالایی',
  newParentId: 'پوشه‌ی مقصد',
  documentTypeId: 'نوع سند',
  label: 'نسخه',
  versionLabel: 'نسخه',
  versionNumber: 'شماره‌ی نسخه',
  fileName: 'نام فایل',
  size: 'حجم',
  fileSize: 'حجم',
  pages: 'تعداد صفحه',
  tags: 'برچسب‌ها',
  username: 'نام کاربری',
  displayName: 'نام نمایشی',
  email: 'ایمیل',
  isActive: 'فعال',
  isSystemAdmin: 'مدیر سامانه',
  permission: 'مجوز',
  permissions: 'مجوزها',
  add: 'افزوده',
  remove: 'برداشته',
  effect: 'اثر',
  inherit: 'به زیرپوشه‌ها هم برسد',
  subjectType: 'نوع گیرنده',
  subjectId: 'گیرنده',
  resourceType: 'نوع موضوع',
  resourceId: 'موضوع',
  userId: 'کاربر',
  groupId: 'گروه',
  roleId: 'نقش',
  recipient: 'گیرنده',
  expiresAt: 'تاریخ انقضا',
  dueAt: 'مهلت',
  step: 'مرحله',
  status: 'وضعیت',
  outcome: 'نتیجه',
  fields: 'فیلدها',
  changedFields: 'فیلدهای تغییرکرده',
  before: 'پیش از تغییر',
  after: 'پس از تغییر',
  moved: 'جابه‌جا شد',
  from: 'از',
  to: 'تا',
  format: 'قالب فایل',
  action: 'رویداد',
  actorType: 'نوع انجام‌دهنده',
  documentId: 'سند',
  entityType: 'نوع موضوع',
  entityId: 'موضوع',
  hideRoutine: 'بدون رویدادهای خودکار',
  seals: 'تعداد مهرهای بررسی‌شده',
  rows: 'تعداد رویدادهای بررسی‌شده',
  problems: 'ناسازگاری‌ها',
  checkedAt: 'زمان بررسی',
  days: 'روز',
  certificateNumber: 'شماره‌ی گواهی',
  recordId: 'رکورد',
  recordClassId: 'کلاس رکورد',
  dispositionId: 'درخواست امحا',
  importJobId: 'کار واردات',
  sourceSystem: 'سامانه‌ی مبدأ',
  errorMessage: 'خطا',
  prefix: 'شناسه‌ی پیوند',
  operation: 'عملیات',
  kind: 'نوع',
  code: 'کد',
  type: 'نوع',
  value: 'مقدار',
};

const enumLabels: Record<string, string> = {
  ...a.outcomes,
  ...a.actorTypes,
  ...reasonLabels,
  Allow: 'اجازه',
  Deny: 'منع',
  bad_password: 'گذرواژه‌ی نادرست',
  unknown_user: 'نام کاربری ناشناخته',
  locked_out: 'حساب موقتاً قفل است',
  legal_hold: 'زیر نگهداری قانونی است',
  not_approved: 'تأیید نشده است',
  grant: 'اعطای دسترسی',
  revoke: 'لغو دسترسی',
  approve: 'تأیید',
  request: 'درخواست',
  destroy: 'امحا',
  'Owner defaults at document creation': 'دسترسی پیش‌فرض مالک هنگام ایجاد سند',
  ...entityTypeLabelsForValues(),
};

function entityTypeLabelsForValues(): Record<string, string> {
  const result: Record<string, string> = {};
  for (const code of ['Document', 'DocumentVersion', 'Category', 'User', 'Group', 'Role', 'DocumentType', 'Workflow', 'Record']) {
    result[code] = entityTypeLabel(code);
  }
  return result;
}

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isoDate = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/;
/** .NET ticks (100 ns since 0001-01-01) of 1970-01-01. */
const unixEpochTicks = 621355968000000000;

export type Field = { key: string; label: string; value: string } | { key: string; label: string; change: [string, string] };

const isScalar = (value: unknown) => value === null || typeof value !== 'object';

export function parseMetadata(json: string): Record<string, unknown> {
  try {
    const value = JSON.parse(json) as unknown;
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** One value as text, or null when there is nothing a person could read (an unnamed id, an empty list). */
export function formatValue(key: string, value: unknown, names: Record<string, string>): string | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'boolean') return value ? 'بله' : 'خیر';
  if (typeof value === 'number') {
    // The seal check stores its time as .NET ticks.
    if (key === 'checkedAt' && value > unixEpochTicks) {
      return formatDateTime(new Date((value - unixEpochTicks) / 10000).toISOString());
    }
    return key === 'size' || key === 'fileSize' ? formatSize(value) : value.toLocaleString('fa-IR');
  }
  if (typeof value === 'string') {
    if (guid.test(value)) return names[value] ?? null;
    if (isoDate.test(value)) return formatDateTime(value);
    if (permissionLabels[value]) return permissionLabel(value);
    return enumLabels[value] ?? value;
  }
  if (Array.isArray(value)) {
    const items = value.map((item) => formatValue(key, item, names)).filter((item): item is string => !!item);
    return items.length ? items.join('، ') : null;
  }
  if (typeof value === 'object') {
    const parts = Object.entries(value as Record<string, unknown>)
      .map(([inner, innerValue]) => {
        const text = formatValue(inner, innerValue, names);
        return text ? `${fieldLabels[inner] ?? inner}: ${text}` : null;
      })
      .filter((part): part is string => !!part);
    return parts.length ? parts.join(' · ') : null;
  }
  return String(value);
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes.toLocaleString('fa-IR')} بایت`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toLocaleString('fa-IR', { maximumFractionDigits: 1 })} کیلوبایت`;
  return `${(bytes / 1024 / 1024).toLocaleString('fa-IR', { maximumFractionDigits: 1 })} مگابایت`;
}

/**
 * The readable fields of a row. A before/after pair becomes one «از … به …» line per key that
 * changed; everything else is a labelled value. Unknown keys and unnamed ids are left out.
 */
export function readableFields(entry: AuditEntry): Field[] {
  const names = entry.names ?? {};
  const metadata = parseMetadata(entry.metadata);
  const fields: Field[] = [];

  const before = metadata.before as Record<string, unknown> | undefined;
  const after = metadata.after as Record<string, unknown> | undefined;
  if (before && after && typeof before === 'object' && typeof after === 'object' && !Array.isArray(before)) {
    for (const key of new Set([...Object.keys(before), ...Object.keys(after)])) {
      const old = formatValue(key, before[key], names) ?? '—';
      const now = formatValue(key, after[key], names) ?? '—';
      if (old !== now) {
        fields.push({ key, label: fieldLabels[key] ?? key, change: [old, now] });
      }
    }
  } else if ('before' in metadata && 'after' in metadata && isScalar(before) && isScalar(after)) {
    // A single value changed, e.g. a watermark.
    fields.push({
      key: 'value',
      label: 'مقدار',
      change: [formatValue('value', before, names) ?? '—', formatValue('value', after, names) ?? '—'],
    });
  }

  for (const [key, value] of Object.entries(metadata)) {
    if (key === 'before' || key === 'after' || !fieldLabels[key]) continue;
    // «Not moved» says nothing; only a move is news.
    if (key === 'moved' && value === false) continue;
    const text = formatValue(key, value, names);
    if (text) fields.push({ key, label: fieldLabels[key], value: text });
  }

  return fields;
}

/** What the row is about, by name: the document, the entity, or a title or name in the metadata. */
export function subjectName(entry: AuditEntry): string | null {
  const names = entry.names ?? {};
  const metadata = parseMetadata(entry.metadata);
  const fromMetadata = ['title', 'documentTitle', 'name', 'username', 'code'].map((key) => metadata[key]).find((value) => typeof value === 'string');
  return (
    (entry.documentId && names[entry.documentId]) ||
    (entry.entityId && names[entry.entityId]) ||
    (typeof fromMetadata === 'string' ? fromMetadata : null)
  );
}

const quoted = (text: string | null | undefined) => (text ? `«${text}»` : null);

/**
 * The row as one line: the action and what it was done to. Grants and revocations say which
 * permission, for whom and where; the subject is left out when it is only the actor again (a login).
 */
export function headline(entry: AuditEntry): { action: string; object: string | null; version: string | null } {
  const names = entry.names ?? {};
  const metadata = parseMetadata(entry.metadata);
  const version = entry.versionId ? names[entry.versionId] ?? null : null;
  const action = actionLabel(entry.action);

  if (entry.action === 'PERMISSION_GRANTED' || entry.action === 'PERMISSION_REVOKED') {
    const permission = typeof metadata.permission === 'string' ? permissionLabel(metadata.permission) : null;
    const subject = typeof metadata.subjectId === 'string' ? names[metadata.subjectId] : null;
    const resource = typeof metadata.resourceId === 'string' ? names[metadata.resourceId] : null;
    const parts = [quoted(permission), subject && `برای ${quoted(subject)}`, resource && `روی ${quoted(resource)}`];
    return { action, object: parts.filter(Boolean).join(' ') || null, version };
  }

  const subject = subjectName(entry);
  const isActor = (!!entry.userId && entry.entityId === entry.userId) || subject === entry.userName;
  return { action, object: isActor ? null : quoted(subject), version };
}

export function actorName(entry: AuditEntry): string {
  if (entry.actorType === 'USER') return entry.userName ?? 'کاربر حذف‌شده';
  return a.actorTypes[entry.actorType] ?? entry.actorType;
}

/** «Chrome روی Windows» from a user agent string; the full string stays in the technical section. */
export function describeUserAgent(agent: string | null): string | null {
  if (!agent) return null;
  const browser =
    /Edg\//.test(agent) ? 'Edge'
    : /OPR\/|Opera/.test(agent) ? 'Opera'
    : /Firefox\//.test(agent) ? 'Firefox'
    : /Chrome\//.test(agent) ? 'Chrome'
    : /Safari\//.test(agent) ? 'Safari'
    : /okhttp|Dalvik/i.test(agent) ? 'برنامه‌ی موبایل'
    : null;
  const system =
    /Android/.test(agent) ? 'Android'
    : /iPhone|iPad/.test(agent) ? 'iOS'
    : /Windows/.test(agent) ? 'Windows'
    : /Mac OS X/.test(agent) ? 'macOS'
    : /Linux/.test(agent) ? 'Linux'
    : null;
  if (browser && system) return `${browser} روی ${system}`;
  return browser ?? system ?? null;
}
