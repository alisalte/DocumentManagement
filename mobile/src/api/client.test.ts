import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { describe, expect, it, vi } from 'vitest';
import { createApiClient, type SessionPort } from './client';

function unauthorized(config: InternalAxiosRequestConfig): AxiosError {
  const response = {
    status: 401,
    statusText: 'Unauthorized',
    data: { code: 'auth.unauthenticated', detail: 'The request is not authenticated.' },
    headers: {},
    config,
  } as AxiosResponse;
  const error = new AxiosError('unauthorized', 'ERR_BAD_REQUEST', config, null, response);
  return error;
}

describe('api client refresh', () => {
  it('refreshes once and retries the original requests', async () => {
    let token = 'old';
    let refreshCount = 0;
    const session: SessionPort = {
      getAccessToken: () => token,
      refresh: async () => {
        refreshCount += 1;
        await new Promise((resolve) => setTimeout(resolve, 10));
        token = 'new';
        return true;
      },
      onSessionExpired: vi.fn(),
    };
    const client = createApiClient(session, 'https://example.test/api/v1');
    client.defaults.adapter = async (config) => {
      const authorization = String(config.headers.get('Authorization') ?? '');
      if (authorization !== 'Bearer new') throw unauthorized(config);
      return { data: { ok: true }, status: 200, statusText: 'OK', headers: {}, config };
    };

    const [first, second] = await Promise.all([client.get('/documents'), client.get('/categories')]);

    expect(first.status).toBe(200);
    expect(second.status).toBe(200);
    expect(refreshCount).toBe(1);
    expect(session.onSessionExpired).not.toHaveBeenCalled();
  });

  it('logs out when refresh fails', async () => {
    const expired = vi.fn();
    const session: SessionPort = {
      getAccessToken: () => 'old',
      refresh: async () => false,
      onSessionExpired: expired,
    };
    const client = createApiClient(session, 'https://example.test/api/v1');
    client.defaults.adapter = async (config) => {
      throw unauthorized(config);
    };

    await expect(client.get('/documents')).rejects.toBeInstanceOf(AxiosError);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('does not refresh a failed login', async () => {
    const refresh = vi.fn(async () => true);
    const client = createApiClient(
      { getAccessToken: () => null, refresh, onSessionExpired: vi.fn() },
      'https://example.test/api/v1',
    );
    client.defaults.adapter = async (config) => {
      const response = {
        status: 401,
        statusText: 'Unauthorized',
        data: { code: 'auth.invalid_credentials', detail: 'The username or password is incorrect.' },
        headers: {},
        config,
      } as AxiosResponse;
      throw new AxiosError('unauthorized', 'ERR_BAD_REQUEST', config, null, response);
    };

    await expect(
      client.post('/auth/login', { username: 'a', password: 'b' }, { skipAuthRefresh: true }),
    ).rejects.toBeInstanceOf(AxiosError);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('does not refresh when the server rejected the password', async () => {
    const refresh = vi.fn(async () => true);
    const client = createApiClient(
      { getAccessToken: () => 'access', refresh, onSessionExpired: vi.fn() },
      'https://example.test/api/v1',
    );
    client.defaults.adapter = async (config) => {
      const response = {
        status: 401,
        statusText: 'Unauthorized',
        data: { code: 'auth.invalid_credentials' },
        headers: {},
        config,
      } as AxiosResponse;
      throw new AxiosError('unauthorized', 'ERR_BAD_REQUEST', config, null, response);
    };

    await expect(client.post('/auth/change-password', {})).rejects.toBeInstanceOf(AxiosError);
    expect(refresh).not.toHaveBeenCalled();
  });
});
