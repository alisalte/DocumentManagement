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
 * instead of calling a guessed host.
 *
 * A path such as `/api/v1` is the Docker/nginx setup: the page and the API share
 * one origin, so the bundle does not bake a host. Any other production URL must
 * be HTTPS.
 */
export function resolveApiUrl(
  value = process.env.EXPO_PUBLIC_API_URL,
  production = isProductionBuild(),
  allowHttp = process.env.EXPO_PUBLIC_ALLOW_HTTP_API === '1',
): string {
  const url = value?.trim().replace(/\/$/, '') ?? '';
  if (!url) return '';
  if (url.startsWith('/')) return url;
  if (production && !allowHttp && !url.startsWith('https://')) {
    throw new Error('EXPO_PUBLIC_API_URL must use HTTPS in production.');
  }
  return url;
}

export const env = {
  get apiUrl() {
    return resolveApiUrl();
  },
};
