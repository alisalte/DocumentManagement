import { expect, type Page } from '@playwright/test';

export const apiUrl = process.env.E2E_API_URL ?? 'http://localhost:5091';

/** The administrator's password after global setup changed it. */
export const adminPassword = 'E2eAdministrator!Changed1';

/** Unique per test and project, because both projects run against one database at once. */
export const unique = (prefix: string) => `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export async function api(path: string, body?: unknown, token?: string, method = body ? 'POST' : 'GET'): Promise<any> {
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  const text = await response.text();
  return text ? JSON.parse(text) : undefined;
}

export async function adminToken(): Promise<string> {
  return (await api('/api/v1/auth/login', { username: 'admin', password: adminPassword })).accessToken;
}

export async function signIn(page: Page, username: string, password: string) {
  await page.goto('/');
  await page.getByLabel('نام کاربری').fill(username);
  await page.getByLabel('گذرواژه').fill(password);
  await page.getByRole('button', { name: 'ورود' }).click();
}

export const isPhone = (page: Page) => (page.viewportSize()?.width ?? 1000) < 900;

/** Opens an administration screen the way a person would: the menu on a desktop, the drawer on a phone. */
export async function openAdmin(page: Page, label: string) {
  if (isPhone(page)) {
    await page.getByRole('button', { name: 'پوشه‌ها' }).click();
    await page.getByRole('link', { name: label, exact: true }).click();
  } else {
    await page.getByRole('button', { name: 'مدیریت', exact: true }).click();
    await page.getByRole('menuitem', { name: label, exact: true }).click();
  }

  await expect(page.getByRole('heading', { level: 1, name: label })).toBeVisible();
}

/** Picks from the search-as-you-type picker (users, groups). */
export async function pick(page: Page, label: string, text: string, option: string | RegExp) {
  const input = page.getByLabel(label);
  await input.fill(text);
  await page.getByRole('option', { name: option }).first().click();
}
