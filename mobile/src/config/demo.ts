import { Platform } from 'react-native';

/** Dev-only UI preview without a live API (`?demo=1` on web). */
export function isDemoPreview(): boolean {
  if (!__DEV__ || Platform.OS !== 'web' || typeof window === 'undefined') return false;
  return new URLSearchParams(window.location.search).get('demo') === '1';
}
