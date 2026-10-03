import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { apiBaseOverride, setApiBaseOverride } from './api-base';
import { resolveApiUrl } from './env';
import { normalizeServerUrl } from './server-url';

const KEY = 'dms.serverUrl';

const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

/** Baked URL, or the address the user saved on this phone. */
export function currentApiUrl(): string {
  const override = apiBaseOverride();
  if (override) return override;
  try {
    return resolveApiUrl();
  } catch {
    return '';
  }
}

export async function loadServerUrl(): Promise<string> {
  if (Platform.OS === 'web') return currentApiUrl();
  const stored = await SecureStore.getItemAsync(KEY, options);
  if (stored) {
    try {
      setApiBaseOverride(normalizeServerUrl(stored));
    } catch {
      setApiBaseOverride(null);
    }
  }
  return currentApiUrl();
}

export async function saveServerUrl(input: string): Promise<string> {
  const url = normalizeServerUrl(input);
  setApiBaseOverride(url);
  if (Platform.OS !== 'web') await SecureStore.setItemAsync(KEY, url, options);
  return url;
}
