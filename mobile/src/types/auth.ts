/** Login and refresh payload. Matches `AuthTokens` / `AuthenticatedUserDto` (camelCase JSON). */
export interface AuthenticatedUser {
  id: string;
  username: string;
  displayName: string;
  isSystemAdmin: boolean;
  mustChangePassword: boolean;
}

export interface AuthTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthenticatedUser;
}

export interface StoredSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthenticatedUser;
}
