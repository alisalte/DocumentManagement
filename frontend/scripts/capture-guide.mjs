/**
 * Photographs the real screens the in-app guide teaches, and records where each
 * arrow should point (percent of the viewport). Run from frontend/:
 *   node scripts/capture-guide.mjs
 * The dev server must already be on http://127.0.0.1:5173.
 */
import { chromium } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const outDir = join(root, 'public/guide');
const layoutPath = join(root, 'src/pages/guideShotLayout.ts');
const base = 'http://127.0.0.1:5173';
const width = 1360;
const height = 1120;

const catContracts = '22222222-2222-4222-8222-222222222222';
const catMinutes = '22222222-2222-4222-8222-222222222223';
const typeId = '33333333-3333-4333-8333-333333333333';
const schemaId = '55555555-5555-4555-8555-555555555555';
const docId = '11111111-1111-4111-8111-111111111111';
const versionId = '44444444-4444-4444-8444-444444444444';
const docDeleted = '11111111-1111-4111-8111-111111111112';
const now = '2026-09-20T08:30:00.000Z';

const categories = [
  {
    id: catContracts,
    parentId: null,
    name: 'قراردادها',
    code: 'CNT',
    description: null,
    depth: 0,
    isActive: true,
    sortOrder: 1,
    canView: true,
    canCreate: true,
  },
  {
    id: catMinutes,
    parentId: null,
    name: 'صورت‌جلسه‌ها',
    code: 'MIN',
    description: null,
    depth: 0,
    isActive: true,
    sortOrder: 2,
    canView: true,
    canCreate: true,
  },
];

const documentType = {
  id: typeId,
  code: 'GENERAL',
  name: 'سند عمومی',
  description: null,
  isActive: true,
  latestPublishedVersionId: schemaId,
  settings: {
    workflowId: null,
    workflowMode: 'None',
    allowedExtensions: [],
    maxUploadBytes: null,
    metadataEditPolicy: 'InPlace',
    allowExternalSharing: true,
  },
};

const schema = {
  documentTypeId: typeId,
  versionId: schemaId,
  versionNumber: 1,
  isPublished: true,
  fields: [
    {
      code: 'contract_no',
      label: { fa: 'شماره قرارداد' },
      type: 'Text',
      isRequired: true,
      isSearchable: true,
      isSortable: false,
      showInList: true,
      isApprovalRelevant: false,
      validation: {},
      options: [],
      displayOrder: 1,
      isActive: true,
    },
  ],
  rules: [],
};

const version = {
  id: versionId,
  versionNumber: 1,
  revisionNumber: 0,
  label: '1.0',
  storageObjectId: '66666666-6666-4666-8666-666666666666',
  fileName: 'contract.pdf',
  mimeType: 'application/pdf',
  fileSize: 248_320,
  sha256: 'abc',
  changeKind: 'Create',
  changeDescription: null,
  approvalStatus: 'Approved',
  isPublished: true,
  createdBy: 'demo',
  createdAt: now,
  isCurrent: true,
  isEffective: true,
  scanStatus: 'Clean',
  schemaVersionId: schemaId,
  metadata: { contract_no: '۱۴۰۴-۱۸' },
};

const document = {
  id: docId,
  title: 'قرارداد همکاری',
  description: 'قرارداد همکاری با شرکت نمونه.',
  categoryId: catContracts,
  categoryName: 'قراردادها',
  documentTypeId: typeId,
  ownerId: 'demo',
  currentVersionId: versionId,
  effectiveVersionId: versionId,
  currentMetadata: { contract_no: '۱۴۰۴-۱۸' },
  currentSchemaVersionId: schemaId,
  latestVersionNumber: 1,
  status: 'Active',
  tags: [{ id: 'tag-1', name: 'قرارداد' }],
  createdBy: 'demo',
  createdAt: now,
  updatedBy: 'demo',
  updatedAt: now,
  deletedAt: null,
  deleteReason: null,
  allowedActions: [
    'DOCUMENT_VIEW',
    'DOCUMENT_DOWNLOAD',
    'DOCUMENT_PRINT',
    'DOCUMENT_EDIT',
    'DOCUMENT_CREATE_VERSION',
    'DOCUMENT_DELETE',
    'DOCUMENT_SHARE',
    'DOCUMENT_SHARE_EXTERNAL',
    'DOCUMENT_MANAGE_PERMISSION',
  ],
};

const listItem = {
  id: docId,
  title: 'قرارداد همکاری',
  categoryId: catContracts,
  documentTypeId: typeId,
  ownerId: 'demo',
  currentVersionLabel: '1.0',
  fileName: 'contract.pdf',
  mimeType: 'application/pdf',
  fileSize: 248_320,
  updatedAt: now,
  deletedAt: null,
  deleteReason: null,
};

const deletedItem = {
  ...listItem,
  id: docDeleted,
  title: 'پیش‌نویس صورت‌جلسه',
  fileName: 'minutes.docx',
  deletedAt: '2026-09-28T11:00:00.000Z',
  deleteReason: 'ثبت تکراری',
};

function json(body) {
  return { status: 200, contentType: 'application/json; charset=utf-8', body: JSON.stringify(body) };
}

function payload(pathname) {
  if (pathname === '/api/v1/categories') return categories;
  if (pathname === '/api/v1/document-types' || pathname.startsWith('/api/v1/document-types?')) return [documentType];
  if (pathname === `/api/v1/document-types/${typeId}/schema`) return schema;
  if (pathname === `/api/v1/document-types/schemas/${schemaId}`) return schema;
  if (pathname === '/api/v1/documents' || pathname.startsWith('/api/v1/documents?')) {
    return { items: [listItem], total: 1, page: 1, pageSize: 25 };
  }
  if (pathname === `/api/v1/documents/${docId}`) return document;
  if (pathname === `/api/v1/documents/${docId}/versions`) return [version];
  if (pathname === `/api/v1/documents/${docId}/shares`) {
    return {
      shares: [
        {
          id: 'share-1',
          versionId,
          versionLabel: '1.0',
          sharedWith: { id: 'u2', displayName: 'سارا محمدی' },
          sharedBy: { id: 'demo', displayName: 'John doe' },
          permissions: ['View', 'Download'],
          createdAt: now,
          expiresAt: '2026-12-01T00:00:00.000Z',
          revokedAt: null,
          message: null,
          state: 'Active',
        },
      ],
      links: [
        {
          id: 'link-1',
          versionId,
          versionLabel: '1.0',
          tokenPrefix: 'a1b2c3',
          label: 'ارسال برای مشتری',
          createdBy: { id: 'demo', displayName: 'John doe' },
          permissions: ['View'],
          requiresPassword: true,
          createdAt: now,
          expiresAt: '2026-10-20T00:00:00.000Z',
          maxAccessCount: 5,
          accessCount: 1,
          lastAccessedAt: null,
          lockedUntil: null,
          revokedAt: null,
          state: 'Active',
        },
      ],
      canShare: true,
      canShareExternal: true,
      canManageAll: true,
    };
  }
  if (pathname === '/api/v1/shares/received') {
    return [
      {
        id: 'recv-1',
        documentId: docId,
        documentTitle: 'قرارداد همکاری',
        versionId,
        versionLabel: '1.0',
        fileName: 'contract.pdf',
        mimeType: 'application/pdf',
        fileSize: 248_320,
        sharedBy: { id: 'u2', displayName: 'سارا محمدی' },
        permissions: ['View', 'Download'],
        createdAt: now,
        expiresAt: '2026-12-01T00:00:00.000Z',
        message: 'لطفاً تا پایان هفته ببینید.',
      },
    ];
  }
  if (pathname === '/api/v1/workflow/tasks') {
    return [
      {
        id: 'task-1',
        instanceId: 'inst-1',
        documentId: docId,
        documentTitle: 'قرارداد همکاری',
        versionId,
        versionLabel: '1.0',
        stepCode: 'manager',
        stepName: 'تأیید مدیر',
        status: 'Pending',
        assignedUserId: 'demo',
        assignedGroupId: null,
        assignedRoleId: null,
        createdAt: now,
        dueAt: '2026-09-18T08:00:00.000Z',
        completedAt: null,
        completedBy: null,
        action: null,
        comment: null,
        forwardedFromTaskId: null,
        allowedActions: ['Approve', 'Reject'],
        canAct: true,
        isOverdue: true,
      },
    ];
  }
  if (pathname === '/api/v1/recycle-bin' || pathname.startsWith('/api/v1/recycle-bin?')) {
    return { items: [deletedItem], total: 1, page: 1, pageSize: 25 };
  }
  if (pathname === '/api/v1/search' || pathname.startsWith('/api/v1/search?')) {
    return {
      hits: [
        {
          documentId: docId,
          versionId,
          label: '1.0',
          title: 'قرارداد همکاری',
          fileName: 'contract.pdf',
          mimeType: 'application/pdf',
          categoryId: catContracts,
          documentTypeId: typeId,
          isCurrent: true,
          isEffective: true,
          approvalStatus: 'Approved',
          updatedAt: now,
          highlights: ['متن <mark>قرارداد</mark> همکاری در فایل'],
          matchCount: 3,
        },
      ],
      total: 1,
      page: 1,
      pageSize: 20,
      facets: {
        category: [{ key: catContracts, count: 1 }],
        documentType: [{ key: typeId, count: 1 }],
        tag: [{ key: 'قرارداد', count: 1 }],
      },
      degraded: false,
    };
  }
  if (pathname === '/api/v1/notifications/unread-count') return { count: 1 };
  if (pathname === '/api/v1/storage/usage') return { usedBytes: 2_400_000_000, quotaBytes: 10_000_000_000 };
  if (pathname === '/api/v1/tags' || pathname.startsWith('/api/v1/tags?')) return [{ id: 'tag-1', name: 'قرارداد' }];
  if (pathname === '/api/v1/admin/search/status') {
    return {
      engineEnabled: true,
      extractorEnabled: true,
      index: 'dms-documents',
      indexedVersions: 128,
      extractions: { Completed: 40, Failed: 2, Pending: 1 },
      engineMode: 'OpenSearch',
      extractorMode: 'LocalTesseract',
      ocr: { ocrFiles: 18, textLayerFiles: 22, emptyFiles: 1, ocrCharacters: 84000, textCharacters: 120000 },
    };
  }
  if (pathname === '/api/v1/admin/search/extractions' || pathname.startsWith('/api/v1/admin/search/extractions?')) {
    return [
      {
        storageObjectId: 'obj-1',
        documentId: docId,
        title: 'قرارداد همکاری',
        fileName: 'contract.pdf',
        status: 'Completed',
        method: 'Ocr',
        charCount: 2400,
        engine: 'tesseract',
        error: null,
        attempts: 1,
        completedAt: now,
      },
      {
        storageObjectId: 'obj-2',
        documentId: docDeleted,
        title: 'اسکن صورت‌جلسه',
        fileName: 'scan-blur.png',
        status: 'Failed',
        method: 'Ocr',
        charCount: 0,
        engine: 'tesseract',
        error: 'tesseract exited 1',
        attempts: 2,
        completedAt: now,
      },
    ];
  }
  if (pathname.includes('/preview')) {
    return { versionId, label: '1.0', status: 'Ready', pageCount: 1, error: null, canPrint: true, canDownload: true };
  }
  return null;
}

function fromFor(x, y) {
  if (x >= 78) return 'left';
  if (x <= 16) return 'right';
  if (y <= 12) return 'bottom';
  if (y >= 88) return 'top';
  return 'top';
}

function separate(arrows) {
  for (let i = 0; i < arrows.length; i++) {
    for (let j = 0; j < i; j++) {
      const a = arrows[i];
      const b = arrows[j];
      if (a.from !== b.from || Math.abs(a.x - b.x) >= 10 || Math.abs(a.y - b.y) >= 4) continue;
      // Keep the label on the page: never flip a marker toward the outer edge.
      if (a.y < 85) a.from = 'bottom';
      else a.from = 'top';
    }
  }
}

const shots = [];

async function capture(page, id, targets) {
  await page.waitForTimeout(350);
  const alerts = await page.getByRole('alert').allTextContents();
  if (alerts.length) console.log(`[alert] ${id}:`, alerts.map((text) => text.trim()).filter(Boolean));

  const arrows = [];
  for (const target of targets) {
      let box = null;
    try {
      box = await target.locator.boundingBox({ timeout: 3000 });
    } catch (error) {
      console.log(`[miss] ${id}: ${target.label} — ${error.message.split('\n')[0]}`);
      continue;
    }
    if (!box || box.y + box.height < 0 || box.y > height) {
      console.log(`[miss] ${id}: ${target.label} box=${box ? `${box.x},${box.y}` : 'null'}`);
      continue;
    }
    const x = Math.round(((box.x + box.width / 2) / width) * 1000) / 10;
    const y = Math.round(((box.y + Math.min(box.height, 36) / 2) / height) * 1000) / 10;
    arrows.push({ label: target.label, x, y, from: fromFor(x, y) });
  }
  separate(arrows);

  await page.screenshot({ path: join(outDir, `${id}.jpg`), type: 'jpeg', quality: 82 });
  shots.push({ id, arrows });
  console.log(`[shot] ${id} (${arrows.length} arrows)`);
}

async function pageImage(browser) {
  const art = await browser.newPage({ viewport: { width: 1000, height: 150 } });
  await art.setContent(`<!doctype html><html lang="fa" dir="rtl"><body style="margin:0;background:#fff;color:#1f1638;font-family:'Noto Naskh Arabic',Tahoma,sans-serif;padding:28px 36px">
    <p style="margin:0 0 8px;font-size:13px;color:#6b7280">قرارداد همکاری · نسخه ۱.۰</p>
    <p style="margin:0;font-size:18px;line-height:1.8">طرفین متعهد می‌شوند مفاد این <b>قرارداد</b> را رعایت کنند. این تصویر، پیش‌نمایش صفحهٔ سند در بایگانی است.</p>
  </body></html>`);
  const png = await art.screenshot({ type: 'png' });
  await art.close();
  return png;
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const png = await pageImage(browser);
const page = await browser.newPage({ viewport: { width, height }, locale: 'fa-IR' });
await page.route('**/api/**', async (route) => {
  const url = new URL(route.request().url());
  const path = url.pathname;
  if (/\/pages\/\d+$/.test(path) || /\/print\/\d+$/.test(path)) {
    await route.fulfill({ status: 200, contentType: 'image/png', body: png });
    return;
  }
  const body = payload(path);
  if (body === null) {
    console.log(`[api] ${route.request().method()} ${path}`);
    const empty = path.split('/').pop()?.includes('-') || path.endsWith('s') ? [] : {};
    await route.fulfill(json(empty));
    return;
  }
  await route.fulfill(json(body));
});

await page.addInitScript(() => {
  const apply = () => {
    const style = document.createElement('style');
    style.textContent = '*,*::before,*::after{animation:none!important;transition:none!important}';
    document.head.append(style);
  };
  if (document.head) apply();
  else document.addEventListener('DOMContentLoaded', apply);
});

mkdirSync(outDir, { recursive: true });
const side = () => page.locator('aside').filter({ hasText: 'Fillo' });

await page.goto(`${base}/`, { waitUntil: 'networkidle' });
await page.getByRole('button', { name: 'ورود' }).waitFor();
await capture(page, 'start-login', [
  { label: 'نام کاربری', locator: page.getByLabel('نام کاربری') },
  { label: 'گذرواژه', locator: page.getByLabel('گذرواژه') },
  { label: 'ورود', locator: page.getByRole('button', { name: 'ورود' }) },
]);

const demo = (path) => page.goto(`${base}${path}${path.includes('?') ? '&' : '?'}demo=1`, { waitUntil: 'networkidle' });

await demo('/');
await page.getByRole('link', { name: 'سند جدید' }).first().waitFor();
await capture(page, 'start-home', [
  { label: 'نشان سیستم', locator: side().getByRole('link', { name: 'Fillo', exact: true }) },
  { label: 'جستجو', locator: page.getByPlaceholder('جستجو…') },
  { label: 'سند جدید', locator: side().getByRole('link', { name: 'سند جدید', exact: true }) },
  { label: 'کارتابل', locator: side().getByRole('link', { name: /^کارتابل/ }) },
  { label: 'تنظیمات', locator: side().getByRole('link', { name: 'تنظیمات', exact: true }) },
]);

await demo(`/?category=${catContracts}`);
await page.getByRole('main').getByRole('heading', { name: /قراردادها/ }).waitFor();
await capture(page, 'browse-list', [
  { label: 'درخت پوشه‌ها', locator: side().getByRole('button', { name: 'قراردادها', exact: true }) },
  { label: 'فیلتر عنوان', locator: page.getByRole('main').getByText('جستجو در عنوان', { exact: true }) },
  { label: 'فهرست اسناد', locator: page.getByText('قرارداد همکاری', { exact: true }) },
  { label: 'سند جدید', locator: page.getByRole('main').getByRole('link', { name: 'سند جدید' }) },
]);

await demo(`/new?category=${catContracts}`);
await page.getByRole('button', { name: 'ثبت سند' }).waitFor();
await capture(page, 'create-form', [
  { label: 'انتخاب فایل', locator: page.getByText('انتخاب فایل', { exact: true }) },
  { label: 'پوشه', locator: page.getByRole('main').getByRole('combobox').nth(0) },
  { label: 'نوع سند', locator: page.getByRole('main').getByRole('combobox').nth(1) },
  { label: 'مشخصات', locator: page.getByRole('heading', { name: 'مشخصات سند' }) },
  { label: 'ثبت سند', locator: page.getByRole('button', { name: 'ثبت سند' }) },
]);

await demo(`/search?q=${encodeURIComponent('قرارداد')}`);
await page.getByText('بار در متن این فایل').waitFor();
await capture(page, 'search-results', [
  { label: 'عبارت جستجو', locator: page.getByRole('main').getByRole('textbox', { name: 'جستجو در اسناد', exact: true }) },
  { label: 'جستجو در متن فایل', locator: page.getByText('جستجو در متن فایل', { exact: true }) },
  { label: 'تعداد تکرار', locator: page.getByText('بار در متن این فایل') },
  { label: 'هایلایت', locator: page.locator('mark') },
  { label: 'فیلتر پوشه', locator: page.getByRole('heading', { name: 'پوشه' }) },
]);

await demo(`/documents/${docId}`);
await page.getByRole('heading', { name: /پیش‌نمایش/ }).waitFor();
await page.locator('img[alt*="صفحه"]').waitFor({ timeout: 8000 }).catch(() => {});
await capture(page, 'document-view', [
  { label: 'افزودن نسخه', locator: page.getByRole('button', { name: 'افزودن نسخه‌ی جدید' }) },
  { label: 'حذف', locator: page.getByRole('button', { name: 'حذف' }) },
  { label: 'پیش‌نمایش', locator: page.getByRole('heading', { name: /پیش‌نمایش/ }) },
  { label: 'تاریخچه نسخه‌ها', locator: page.getByRole('heading', { name: 'تاریخچه‌ی نسخه‌ها' }) },
]);

await page.getByRole('heading', { name: 'اشتراک‌ها' }).scrollIntoViewIfNeeded();
await page.waitForTimeout(200);
await capture(page, 'document-share', [
  { label: 'اشتراک‌گذاری', locator: page.getByRole('button', { name: 'اشتراک‌گذاری' }) },
  { label: 'پیوند بیرونی', locator: page.getByText('پیوند بیرونی').first() },
  { label: 'گیرنده', locator: page.getByText('سارا محمدی').first() },
]);

await demo('/tasks');
await page.getByRole('button', { name: 'تأیید' }).waitFor();
await capture(page, 'tasks-inbox', [
  { label: 'کارتابل', locator: side().getByRole('link', { name: /^کارتابل/ }) },
  { label: 'کار باز', locator: page.getByRole('main').getByText('قرارداد همکاری') },
  { label: 'تأیید', locator: page.getByRole('button', { name: 'تأیید' }) },
  { label: 'رد', locator: page.getByRole('main').getByRole('button', { name: 'رد', exact: true }) },
]);

await demo('/shared');
await page.getByRole('main').getByRole('heading', { name: 'اشتراک‌شده با من', exact: true }).waitFor();
await capture(page, 'share-inbox', [
  { label: 'اشتراک‌شده با من', locator: side().getByRole('link', { name: 'اشتراک‌شده با من', exact: true }) },
  { label: 'سند اشتراک‌شده', locator: page.getByRole('main').getByText('قرارداد همکاری') },
]);

await demo('/recycle-bin');
await page.getByRole('button', { name: 'بازگردانی' }).waitFor();
await capture(page, 'recycle-bin', [
  { label: 'سطل بازیافت', locator: side().getByRole('link', { name: 'سطل بازیافت', exact: true }) },
  { label: 'سند حذف‌شده', locator: page.getByText('پیش‌نویس صورت‌جلسه') },
  { label: 'بازگردانی', locator: page.getByRole('button', { name: 'بازگردانی' }) },
]);

await demo('/settings/categories');
await page.getByRole('tab', { name: 'پوشه‌ها' }).waitFor();
await capture(page, 'admin-folders', [
  { label: 'تنظیمات', locator: side().getByRole('link', { name: 'تنظیمات', exact: true }) },
  { label: 'تب پوشه‌ها', locator: page.getByRole('tab', { name: 'پوشه‌ها' }) },
  { label: 'دسترسی‌ها', locator: page.getByRole('button', { name: 'دسترسی‌ها' }).first() },
]);

await demo('/settings/search');
await page.getByRole('main').getByRole('heading', { name: 'جستجو و نمایه', exact: true }).waitFor();
await page.getByText('scan-blur.png').waitFor();
await capture(page, 'admin-ocr', [
  { label: 'تب جستجو و نمایه', locator: page.getByRole('tab', { name: 'جستجو و نمایه' }) },
  { label: 'فایل‌های OCR', locator: page.getByText('فایل‌های OCR') },
  { label: 'خطای استخراج', locator: page.getByText('scan-blur.png') },
]);

await browser.close();

const body = `/* Generated by frontend/scripts/capture-guide.mjs. Arrow positions are percentages of each photo. */
export type GuideArrowFrom = 'top' | 'bottom' | 'left' | 'right';

export type GuideArrow = {
  label: string;
  x: number;
  y: number;
  from: GuideArrowFrom;
};

export const guideShotLayout: Record<string, { arrows: GuideArrow[] }> = ${JSON.stringify(
  Object.fromEntries(shots.map((shot) => [shot.id, { arrows: shot.arrows }])),
  null,
  2,
)};
`;
writeFileSync(layoutPath, body);
console.log(`wrote ${shots.length} shots`);
