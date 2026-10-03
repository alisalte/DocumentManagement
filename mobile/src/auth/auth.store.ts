import type { AuthTokens, AuthenticatedUser, StoredSession } from '../types/auth';
import type { TokenStorage } from '../storage/secure-storage';

export interface AuthState {
  user: AuthenticatedUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
}

export interface AuthApi {
  login(username: string, password: string): Promise<AuthTokens>;
  refresh(refreshToken: string): Promise<AuthTokens>;
  logout(refreshToken: string): Promise<void>;
}

const signedOut: AuthState = { user: null, isAuthenticated: false, isLoading: false };

export interface AuthController {
  getState(): AuthState;
  getAccessToken(): string | null;
  subscribe(listener: () => void): () => void;
  login(username: string, password: string): Promise<void>;
  logout(): Promise<void>;
  restoreSession(): Promise<void>;
  refreshSession(): Promise<boolean>;
  clearLocal(): Promise<void>;
  /** Dev-only: signed-in UI without talking to the API. */
  enterDemo(user?: AuthenticatedUser): void;
}

const skewMs = 30_000;

export function createAuthController(deps: {
  storage: TokenStorage;
  api: AuthApi;
  now?: () => number;
}): AuthController {
  const now = deps.now ?? Date.now;
  let state: AuthState = { ...signedOut, isLoading: true };
  let accessToken: string | null = null;
  let refreshInFlight: Promise<boolean> | null = null;
  const listeners = new Set<() => void>();

  function emit(next: AuthState) {
    state = next;
    listeners.forEach((listener) => listener());
  }

  function applyTokens(tokens: AuthTokens) {
    accessToken = tokens.accessToken;
    emit({ user: tokens.user, isAuthenticated: true, isLoading: false });
  }

  async function clearLocal() {
    accessToken = null;
    await deps.storage.clear();
    emit(signedOut);
  }

  async function refreshSession() {
    refreshInFlight ??= (async () => {
      const stored = await deps.storage.read();
      if (!stored?.refreshToken) {
        await clearLocal();
        return false;
      }
      try {
        const tokens = await deps.api.refresh(stored.refreshToken);
        await deps.storage.save(tokens);
        applyTokens(tokens);
        return true;
      } catch {
        await clearLocal();
        return false;
      }
    })().finally(() => {
      refreshInFlight = null;
    });
    return refreshInFlight;
  }

  function accessStillValid(stored: StoredSession): boolean {
    const expires = Date.parse(stored.accessTokenExpiresAt);
    return Number.isFinite(expires) && expires - skewMs > now();
  }

  return {
    getState: () => state,
    getAccessToken: () => accessToken,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    async login(username, password) {
      const tokens = await deps.api.login(username, password);
      await deps.storage.save(tokens);
      applyTokens(tokens);
    },
    async logout() {
      const stored = await deps.storage.read();
      if (stored?.refreshToken) {
        // An expired access token would make logout refresh first and rotate the
        // refresh token. Revoke the token that is current after that rotation.
        if (!accessToken || !accessStillValid(stored)) {
          const refreshed = await refreshSession();
          if (!refreshed) return;
        }
        const latest = await deps.storage.read();
        if (latest?.refreshToken && accessToken) {
          try {
            await deps.api.logout(latest.refreshToken);
          } catch {
            // Local sign-out still happens when the server is unreachable.
          }
        }
      }
      await clearLocal();
    },
    async restoreSession() {
      const stored = await deps.storage.read();
      if (!stored) {
        accessToken = null;
        emit(signedOut);
        return;
      }
      if (accessStillValid(stored)) {
        accessToken = stored.accessToken;
        emit({ user: stored.user, isAuthenticated: true, isLoading: false });
        return;
      }
      await refreshSession();
    },
    refreshSession,
    clearLocal,
    enterDemo(user) {
      accessToken = null;
      emit({
        user: user ?? {
          id: 'demo',
          username: 'demo',
          displayName: 'کاربر نمونه',
          isSystemAdmin: false,
          mustChangePassword: false,
        },
        isAuthenticated: true,
        isLoading: false,
      });
    },
  };
}
