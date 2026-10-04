import { Platform } from 'react-native';
import * as Sharing from 'expo-sharing';
import type { PreparedFile } from './filing';

/**
 * Saves the prepared scan on the device.
 * On web, `expo-sharing` only passes a URL to `navigator.share`; blob: URLs open a
 * blank page on phones. Share the File bytes when possible, otherwise download.
 */
export async function savePreparedFile(file: PreparedFile): Promise<void> {
  if (Platform.OS === 'web') {
    await saveOnWeb(file);
    return;
  }

  const available = await Sharing.isAvailableAsync();
  if (!available) {
    throw new Error('اشتراک‌گذاری روی این دستگاه در دسترس نیست.');
  }
  await Sharing.shareAsync(file.uri, {
    mimeType: file.mimeType,
    dialogTitle: 'سند اسکن‌شده',
  });
}

async function saveOnWeb(file: PreparedFile): Promise<void> {
  if (typeof document === 'undefined') {
    throw new Error('ذخیره فایل در این محیط پشتیبانی نمی‌شود.');
  }

  const response = await fetch(file.uri);
  if (!response.ok) {
    throw new Error('خواندن فایل برای ذخیره ناموفق بود.');
  }
  const blob = await response.blob();
  const shareFile =
    typeof File !== 'undefined' ? new File([blob], file.fileName, { type: file.mimeType || blob.type }) : null;

  const canShareFiles =
    !!shareFile &&
    typeof navigator !== 'undefined' &&
    typeof navigator.canShare === 'function' &&
    typeof navigator.share === 'function' &&
    navigator.canShare({ files: [shareFile] });

  if (canShareFiles && shareFile) {
    await navigator.share({ files: [shareFile], title: 'سند اسکن‌شده' });
    return;
  }

  downloadBlob(blob, file.fileName);
}

function downloadBlob(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    // Give the browser a tick to start the download before revoking.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 1_000);
  }
}
