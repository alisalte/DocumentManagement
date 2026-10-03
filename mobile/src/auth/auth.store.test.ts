import { describe, expect, it, vi } from 'vitest';
import { createAuthController } from './auth.store';
import type { TokenStorage } from '../storage/secure-storage';
import type { AuthTokens, StoredSession } from '../types/auth';
import { ApiError } from '../utils/errors';

function tokens(patch: Partial<AuthTokens> = {}): AuthTokens {
  return {
    accessToken: 'access',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshToken: 'refresh',
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    user: {
      id: 'user-1',
      username: 'admin',
      displayName: 'مدیر',
      isSystemAdmin: false,
      mustChangePassword: false,
    },
    ...patch,
  };
}

function memoryStorage(initial: StoredSession | null = null): TokenStorage & { current: () => StoredSession | null } {
  let session = initial;
  return {
    current: () => session,
    async save(next) {
      session = next;
    },
    async read() {
      return session;
    },
    async clear() {
      session = null;
    },
  };
}

describe('auth controller', () => {
  it('logs in and keeps the user', async () => {
    const storage = memoryStorage();
    const issued = tokens();
    const controller = createAuthController({
      storage,
      api: { login: vi.fn(async () => issued), refresh: vi.fn(), logout: vi.fn() },
    });

    await controller.login('admin', 'secret');

    expect(controller.getState().isAuthenticated).toBe(true);
    expect(controller.getState().user?.username).toBe('admin');
    expect(controller.getAccessToken()).toBe('access');
    expect(storage.current()?.refreshToken).toBe('refresh');
  });

  it('keeps the signed-out state when login fails', async () => {
    const controller = createAuthController({
      storage: memoryStorage(),
      api: {
        login: vi.fn(async () => {
          throw new ApiError(401, 'auth.invalid_credentials', 'no');
        }),
        refresh: vi.fn(),
        logout: vi.fn(),
      },
    });

    await expect(controller.login('admin', 'bad')).rejects.toBeInstanceOf(ApiError);
    expect(controller.getState().isAuthenticated).toBe(false);
    expect(controller.getAccessToken()).toBeNull();
  });

  it('restores a session that still has a valid access token', async () => {
    const stored = tokens();
    const refresh = vi.fn();
    const controller = createAuthController({
      storage: memoryStorage(stored),
      api: { login: vi.fn(), refresh, logout: vi.fn() },
    });

    await controller.restoreSession();

    expect(refresh).not.toHaveBeenCalled();
    expect(controller.getState().isAuthenticated).toBe(true);
    expect(controller.getAccessToken()).toBe('access');
  });

  it('refreshes an expired access token while restoring', async () => {
    const stored = tokens({ accessTokenExpiresAt: new Date(Date.now() - 60_000).toISOString() });
    const rotated = tokens({ accessToken: 'next', refreshToken: 'next-refresh' });
    const refresh = vi.fn(async () => rotated);
    const controller = createAuthController({
      storage: memoryStorage(stored),
      api: { login: vi.fn(), refresh, logout: vi.fn() },
    });

    await controller.restoreSession();

    expect(refresh).toHaveBeenCalledWith('refresh');
    expect(controller.getAccessToken()).toBe('next');
  });

  it('refreshes an expired access token before logout, then revokes the new refresh token', async () => {
    const storage = memoryStorage(
      tokens({
        accessTokenExpiresAt: new Date(Date.now() - 60_000).toISOString(),
        refreshToken: 'old-refresh',
      }),
    );
    const logout = vi.fn(async () => undefined);
    const controller = createAuthController({
      storage,
      api: {
        login: vi.fn(),
        refresh: vi.fn(async () => tokens({ accessToken: 'fresh', refreshToken: 'rotated' })),
        logout,
      },
    });

    await controller.logout();

    expect(logout).toHaveBeenCalledWith('rotated');
    expect(controller.getState().isAuthenticated).toBe(false);
    expect(storage.current()).toBeNull();
  });

  it('logs out and clears stored tokens even when the server call fails', async () => {
    const storage = memoryStorage(tokens());
    const controller = createAuthController({
      storage,
      api: {
        login: vi.fn(),
        refresh: vi.fn(),
        logout: vi.fn(async () => {
          throw new ApiError(0, 'network', 'network');
        }),
      },
    });
    await controller.restoreSession();

    await controller.logout();

    expect(controller.getState().isAuthenticated).toBe(false);
    expect(storage.current()).toBeNull();
    expect(controller.getAccessToken()).toBeNull();
  });

  it('clears the session when refresh fails', async () => {
    const storage = memoryStorage(tokens({ accessTokenExpiresAt: new Date(Date.now() - 1000).toISOString() }));
    const controller = createAuthController({
      storage,
      api: {
        login: vi.fn(),
        refresh: vi.fn(async () => {
          throw new ApiError(401, 'auth.invalid_refresh_token', 'no');
        }),
        logout: vi.fn(),
      },
    });

    await expect(controller.restoreSession()).resolves.toBeUndefined();
    expect(controller.getState().isAuthenticated).toBe(false);
    expect(storage.current()).toBeNull();
  });

  it('shares one refresh call between concurrent callers', async () => {
    let release: (value: AuthTokens) => void = () => undefined;
    const refresh = vi.fn(
      () =>
        new Promise<AuthTokens>((resolve) => {
          release = resolve;
        }),
    );
    const controller = createAuthController({
      storage: memoryStorage(tokens()),
      api: { login: vi.fn(), refresh, logout: vi.fn() },
    });

    const first = controller.refreshSession();
    const second = controller.refreshSession();
    await vi.waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    release(tokens({ accessToken: 'shared' }));
    await Promise.all([first, second]);

    expect(controller.getAccessToken()).toBe('shared');
  });
});
