import { File, Paths } from 'expo-file-system';
import type { ScanPage } from './scanner.types';

function extensionOf(uri: string): string {
  const path = uri.split('?')[0]?.toLowerCase() ?? '';
  if (path.endsWith('.png')) return 'png';
  if (path.endsWith('.webp')) return 'webp';
  return 'jpg';
}

/** Copies a capture into the app cache so preview still has it after the camera temp file goes away. */
export async function persistPage(photo: { uri: string; width: number; height: number }): Promise<ScanPage> {
  const id = crypto.randomUUID();
  const ext = extensionOf(photo.uri);
  try {
    const source = new File(photo.uri);
    const dest = new File(Paths.cache, `scan-${id}.${ext}`);
    await source.copy(dest);
    return { id, uri: dest.uri, width: photo.width, height: photo.height };
  } catch {
    return { id, uri: photo.uri, width: photo.width, height: photo.height };
  }
}
