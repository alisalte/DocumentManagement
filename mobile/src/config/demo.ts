import { Platform } from 'react-native';

/** Dev-only UI preview without a live API (`?demo=1` on web). */
export function isDemoPreview(): boolean {
  if (!__DEV__ || Platform.OS !== 'web' || typeof window === 'undefined') return false;
  return new URLSearchParams(window.location.search).get('demo') === '1';
}

/** Sample folders and documents for the demo preview. */
export const demoCategories = [
  { id: 'c1', parentId: null, name: 'قراردادها', code: 'c1', depth: 1, isActive: true, canView: true, canCreate: true },
  { id: 'c2', parentId: null, name: 'منابع انسانی', code: 'c2', depth: 1, isActive: true, canView: true, canCreate: true },
  { id: 'c3', parentId: null, name: 'مالی', code: 'c3', depth: 1, isActive: true, canView: true, canCreate: true },
];

export const demoDocuments = [
  ['قرارداد خرید تجهیزات', 'contract.pdf', 'application/pdf', 'V1.2', 240_000],
  ['صورت‌جلسه هیئت مدیره', 'minutes.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'V2.0', 88_000],
  ['گزارش مالی سه‌ماهه', 'report.xlsx', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'V1.0', 512_000],
  ['اسکن نامه‌ی اداری', 'letter.jpg', 'image/jpeg', 'V1.0', 1_400_000],
].map(([title, fileName, mimeType, currentVersionLabel, fileSize], index) => ({
  id: `demo-${index}`,
  title: title as string,
  categoryId: demoCategories[index % 3]!.id,
  documentTypeId: 't',
  ownerId: 'u',
  currentVersionLabel: currentVersionLabel as string,
  fileName: fileName as string,
  mimeType: mimeType as string,
  fileSize: fileSize as number,
  updatedAt: new Date(Date.now() - index * 86_400_000).toISOString(),
  deletedAt: null,
  deleteReason: null,
}));
