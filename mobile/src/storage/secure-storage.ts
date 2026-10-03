import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import type { AuthTokens, StoredSession } from '../types/auth';

const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

const keys = {
  accessToken: 'dms.accessToken',
  accessTokenExpiresAt: 'dms.accessTokenExpiresAt',
  refreshToken: 'dms.refreshToken',
  refreshTokenExpiresAt: 'dms.refreshTokenExpiresAt',
  user: 'dms.user',
} as const;

export interface TokenStorage {
  save(tokens: AuthTokens): Promise<void>;
  read(): Promise<StoredSession | null>;
  clear(): Promise<void>;
}

function browserStorage(): Storage | null {
  if (typeof localStorage === 'undefined') return null;
  return localStorage;
}

async function put(key: string, value: string): Promise<void> {
  if (Platform.OS === 'web') {
    browserStorage()?.setItem(key, value);
    return;
  }
  await SecureStore.setItemAsync(key, value, options);
}

async function readKey(key: string): Promise<string | null> {
  if (Platform.OS === 'web') return browserStorage()?.getItem(key) ?? null;
  return SecureStore.getItemAsync(key, options);
}

async function remove(key: string): Promise<void> {
  if (Platform.OS === 'web') {
    browserStorage()?.removeItem(key);
    return;
  }
  await SecureStore.deleteItemAsync(key, options);
}

/**
 * Tokens live in SecureStore on a device. The web build has no keychain, so it
 * uses localStorage. Passwords are never written.
 */
export const secureTokenStorage: TokenStorage = {
  async save(tokens) {
    await Promise.all([
      put(keys.accessToken, tokens.accessToken),
      put(keys.accessTokenExpiresAt, tokens.accessTokenExpiresAt),
      put(keys.refreshToken, tokens.refreshToken),
      put(keys.refreshTokenExpiresAt, tokens.refreshTokenExpiresAt),
      put(keys.user, JSON.stringify(tokens.user)),
    ]);
  },

  async read() {
    const [accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, userJson] = await Promise.all([
      readKey(keys.accessToken),
      readKey(keys.accessTokenExpiresAt),
      readKey(keys.refreshToken),
      readKey(keys.refreshTokenExpiresAt),
      readKey(keys.user),
    ]);

    if (!refreshToken || !accessToken || !accessTokenExpiresAt || !refreshTokenExpiresAt || !userJson) {
      return null;
    }

    try {
      return {
        accessToken,
        accessTokenExpiresAt,
        refreshToken,
        refreshTokenExpiresAt,
        user: JSON.parse(userJson) as StoredSession['user'],
      };
    } catch {
      return null;
    }
  },

  async clear() {
    await Promise.all(Object.values(keys).map((key) => remove(key)));
  },
};
