import { adminPassword, api } from './support';

/**
 * The seeded administrator has to change the password at first sign-in. Done once here, through
 * the API; the forced-change screen itself is exercised with an account the tests create.
 */
export default async function globalSetup() {
  const login = await api('/api/v1/auth/login', { username: 'admin', password: process.env.E2E_ADMIN_PASSWORD });
  if (!login.user.mustChangePassword) return;
  await api('/api/v1/auth/change-password', { currentPassword: process.env.E2E_ADMIN_PASSWORD, newPassword: adminPassword }, login.accessToken);
}
