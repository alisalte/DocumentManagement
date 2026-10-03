import { http, withApi } from './client';
import type { AuthTokens } from '../types/auth';

/** `POST /api/v1/auth/login` — `{ username, password }` → `AuthTokens`. */
export function login(username: string, password: string): Promise<AuthTokens> {
  return withApi(async () => {
    const response = await http.post<AuthTokens>(
      '/auth/login',
      { username, password },
      { skipAuthRefresh: true },
    );
    return response.data;
  });
}

/** `POST /api/v1/auth/refresh` — `{ refreshToken }` rotates both tokens. */
export function refresh(refreshToken: string): Promise<AuthTokens> {
  return withApi(async () => {
    const response = await http.post<AuthTokens>(
      '/auth/refresh',
      { refreshToken },
      { skipAuthRefresh: true },
    );
    return response.data;
  });
}

/** `POST /api/v1/auth/logout` — revokes the refresh token. Requires the access token. */
export function logout(refreshToken: string): Promise<void> {
  return withApi(async () => {
    await http.post('/auth/logout', { refreshToken });
  });
}

/** `POST /api/v1/auth/change-password`. The server then revokes every session. */
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return withApi(async () => {
    await http.post('/auth/change-password', { currentPassword, newPassword });
  });
}
