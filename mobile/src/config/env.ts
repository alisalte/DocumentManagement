const raw = process.env.EXPO_PUBLIC_API_URL?.trim().replace(/\/$/, '') ?? '';

/** Test runs are not production builds, even when `__DEV__` is unset. */
export function isProductionBuild(): boolean {
  if (process.env.NODE_ENV === 'test') return false;
  if (typeof __DEV__ !== 'undefined') return !__DEV__;
  return process.env.NODE_ENV === 'production';
}

export function isDevLogEnabled(): boolean {
  return !isProductionBuild() && process.env.NODE_ENV !== 'test';
}

/**
 * API origin including `/api/v1`. Empty when unset so the UI can explain it
 * instead of calling a guessed host. Production builds refuse plain HTTP.
 */
export function resolveApiUrl(): string {
  if (!raw) return '';
  if (isProductionBuild() && !raw.startsWith('https://')) {
    throw new Error('EXPO_PUBLIC_API_URL must use HTTPS in production.');
  }
  return raw;
}

export const env = {
  get apiUrl() {
    return resolveApiUrl();
  },
};
