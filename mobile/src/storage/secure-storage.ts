import * as SecureStore from 'expo-secure-store';
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

async function put(key: string, value: string): Promise<void> {
  await SecureStore.setItemAsync(key, value, options);
}

async function remove(key: string): Promise<void> {
  await SecureStore.deleteItemAsync(key, options);
}

/** Tokens live in SecureStore. Passwords are never written. */
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
      SecureStore.getItemAsync(keys.accessToken, options),
      SecureStore.getItemAsync(keys.accessTokenExpiresAt, options),
      SecureStore.getItemAsync(keys.refreshToken, options),
      SecureStore.getItemAsync(keys.refreshTokenExpiresAt, options),
      SecureStore.getItemAsync(keys.user, options),
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
