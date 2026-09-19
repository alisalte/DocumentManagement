/**
 * Thin API client.
 *
 * The access token is short lived and kept in memory only; the refresh token lives in
 * sessionStorage for the dev shell. Permissions returned by the API are used to hide UI that
 * would fail anyway, never to decide access: the server is the only authority.
 */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080';

let accessToken: string | null = null;

export interface CurrentUser {
  id: string;
  username: string;
  displayName: string;
  email: string | null;
  isSystemAdmin: boolean;
  mustChangePassword: boolean;
  groupIds: string[];
  systemPermissions: string[];
}

export interface AuthTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: {
    id: string;
    username: string;
    displayName: string;
    isSystemAdmin: boolean;
    mustChangePassword: boolean;
  };
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const response = await fetch(`${baseUrl}${path}`, { ...init, headers });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new ApiError(response.status, problem.code, problem.detail ?? response.statusText);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  async login(username: string, password: string): Promise<AuthTokens> {
    const tokens = await request<AuthTokens>('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });

    accessToken = tokens.accessToken;
    sessionStorage.setItem('dms.refreshToken', tokens.refreshToken);
    return tokens;
  },

  async me(): Promise<CurrentUser> {
    return request<CurrentUser>('/api/v1/auth/me');
  },

  async logout(): Promise<void> {
    const refreshToken = sessionStorage.getItem('dms.refreshToken');
    if (refreshToken) {
      await request<void>('/api/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      }).catch(() => undefined);
    }

    accessToken = null;
    sessionStorage.removeItem('dms.refreshToken');
  },

  isSignedIn: () => accessToken !== null,
};
