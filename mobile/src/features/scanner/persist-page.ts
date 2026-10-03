import * as FileSystem from 'expo-file-system/legacy';
import { extensionOf, newScanId } from './scan-id';
import type { ScanPage } from './scanner.types';

export { extensionOf, newScanId } from './scan-id';

/** Copies a capture into the app cache so preview still has it after the camera temp file goes away. */
export async function persistPage(photo: { uri: string; width: number; height: number }): Promise<ScanPage> {
  const id = newScanId();
  const ext = extensionOf(photo.uri);
  const cacheDir = FileSystem.cacheDirectory;
  if (!cacheDir) {
    return { id, uri: photo.uri, width: photo.width, height: photo.height };
  }

  const dest = `${cacheDir}scan-${id}.${ext}`;
  try {
    await FileSystem.copyAsync({ from: photo.uri, to: dest });
    return { id, uri: dest, width: photo.width, height: photo.height };
  } catch {
    // Camera temp URIs sometimes cannot be copied; keep the original long enough for this session.
    return { id, uri: photo.uri, width: photo.width, height: photo.height };
  }
}
