import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import { apiBaseOverride } from '../config/api-base';
import { resolveApiUrl } from '../config/env';
import type { ProblemDetails } from '../types/api';
import { shouldRefreshAfterUnauthorized, toApiError } from '../utils/errors';

export interface SessionPort {
  getAccessToken(): string | null;
  refresh(): Promise<boolean>;
  onSessionExpired(): void;
}

export const sessionPort: SessionPort = {
  getAccessToken: () => null,
  refresh: async () => false,
  onSessionExpired: () => undefined,
};

export function bindSession(next: SessionPort): void {
  sessionPort.getAccessToken = () => next.getAccessToken();
  sessionPort.refresh = () => next.refresh();
  sessionPort.onSessionExpired = () => next.onSessionExpired();
}

function isAuthExchange(url: string | undefined): boolean {
  return !!url && (url.includes('/auth/login') || url.includes('/auth/refresh'));
}

export function attachAuth(client: AxiosInstance, session: SessionPort): void {
  let refreshing: Promise<boolean> | null = null;
  const retried = new WeakSet<InternalAxiosRequestConfig>();

  const refreshOnce = () => {
    refreshing ??= session.refresh().finally(() => {
      refreshing = null;
    });
    return refreshing;
  };

  client.interceptors.request.use((config) => {
    const override = apiBaseOverride();
    if (override) config.baseURL = override;
    if (!config.skipAuthRefresh) {
      const token = session.getAccessToken();
      if (token) {
        config.headers.set('Authorization', `Bearer ${token}`);
      }
    }
    return config;
  });

  client.interceptors.response.use(
    (response) => response,
    async (error: AxiosError) => {
      const config = error.config;
      if (!config) return Promise.reject(error);

      const status = error.response?.status;
      const code = (error.response?.data as ProblemDetails | undefined)?.code;
      const canRefresh =
        status === 401 &&
        !config.skipAuthRefresh &&
        !isAuthExchange(config.url) &&
        !retried.has(config) &&
        shouldRefreshAfterUnauthorized(code);

      if (!canRefresh) return Promise.reject(error);

      retried.add(config);
      const refreshed = await refreshOnce();
      if (!refreshed) {
        session.onSessionExpired();
        return Promise.reject(error);
      }

      const token = session.getAccessToken();
      if (token) config.headers.set('Authorization', `Bearer ${token}`);
      return client.request(config);
    },
  );
}

function bakedBaseUrl(): string {
  try {
    return resolveApiUrl();
  } catch {
    return '';
  }
}

export function createApiClient(session: SessionPort, baseURL = bakedBaseUrl()): AxiosInstance {
  const client = axios.create({
    baseURL,
    timeout: 120_000,
    headers: { Accept: 'application/json' },
  });
  attachAuth(client, session);
  return client;
}

export const http = createApiClient(sessionPort);

export async function withApi<T>(work: () => Promise<T>): Promise<T> {
  const base = apiBaseOverride() || bakedBaseUrl();
  if (!base) {
    throw toApiError(new Error('EXPO_PUBLIC_API_URL is not set'));
  }
  try {
    return await work();
  } catch (error) {
    throw toApiError(error);
  }
}
