import { expect, test } from '@playwright/test';
import { adminPassword, adminToken, api, openAdmin, pick, signIn, unique } from './support';

/**
 * Phase 8 end to end: the administration screens, used as an administrator would, on a phone and
 * on a desktop. Each test makes its own users, groups, roles and categories.
 */
test.describe('administration', () => {
  test('a new user is created and must change the password at first sign-in', async ({ page, browser }) => {
    const username = unique('e2e.user.');
    await signIn(page, 'admin', adminPassword);
    await openAdmin(page, 'کاربران');

    await page.getByRole('button', { name: 'کاربر جدید' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('نام کاربری').fill(username);
    await dialog.getByLabel('نام نمایشی').fill('کاربر آزمایشی');
    await dialog.getByLabel('گذرواژه‌ی اولیه').fill('FirstPassword!123');
    await dialog.getByRole('button', { name: 'ایجاد' }).click();

    // The new user's details open right away.
    await expect(page.getByRole('dialog').getByText(username)).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: 'بستن' }).click();

    // First sign-in: nothing but the password change.
    const context = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
    const newcomer = await context.newPage();
    await signIn(newcomer, username, 'FirstPassword!123');
    await expect(newcomer.getByText('پیش از ادامه، گذرواژه‌ی تازه‌ای انتخاب کنید.')).toBeVisible();
    await newcomer.getByLabel('گذرواژه‌ی فعلی').fill('FirstPassword!123');
    await newcomer.getByLabel('گذرواژه‌ی تازه', { exact: true }).fill('SecondPassword!456');
    await newcomer.getByLabel('تکرار گذرواژه‌ی تازه').fill('SecondPassword!456');
    await newcomer.getByRole('button', { name: 'تغییر گذرواژه' }).click();
    await expect(newcomer.getByText('پیش از ادامه، گذرواژه‌ی تازه‌ای انتخاب کنید.')).toBeHidden();
    await expect(newcomer.getByRole('button', { name: 'اعلان‌ها' })).toBeVisible();
    await context.close();
  });

  test('a group is created and gets a member', async ({ page }) => {
    const token = await adminToken();
    const username = unique('e2e.member.');
    await api('/api/v1/admin/users', { username, displayName: `عضو ${username}`, email: null, password: 'MemberPassword!123', isSystemAdmin: false, mustChangePassword: false }, token);

    const code = unique('G').toUpperCase().slice(0, 20);
    await signIn(page, 'admin', adminPassword);
    await openAdmin(page, 'گروه‌ها');
    await page.getByRole('button', { name: 'گروه جدید' }).click();
    await page.getByRole('dialog').getByLabel('کد').fill(code);
    await page.getByRole('dialog').getByLabel('نام').fill(`گروه ${code}`);
    await page.getByRole('dialog').getByRole('button', { name: 'ایجاد' }).click();

    await page.getByText(`گروه ${code}`).click();
    const dialog = page.getByRole('dialog');
    await pick(page, 'افزودن عضو', username, new RegExp(username.replace(/\./g, '\\.')));
    await dialog.getByRole('button', { name: 'افزودن', exact: true }).click();
    await expect(dialog.getByText(`عضو ${username}`)).toBeVisible();
  });

  test('a category gets a permission and "why?" explains it', async ({ page }) => {
    const token = await adminToken();
    const username = unique('e2e.reader.');
    await api('/api/v1/admin/users', { username, displayName: `خواننده ${username}`, email: null, password: 'ReaderPassword!123', isSystemAdmin: false, mustChangePassword: false }, token);
    const categoryName = `پوشه ${unique('')}`;

    await signIn(page, 'admin', adminPassword);
    await openAdmin(page, 'پوشه‌ها');
    await page.getByRole('button', { name: 'پوشه‌ی جدید' }).click();
    await page.getByRole('dialog').getByLabel('نام').fill(categoryName);
    await page.getByRole('dialog').getByLabel('کد').fill(unique('C').toUpperCase().slice(0, 20));
    await page.getByRole('dialog').getByRole('button', { name: 'ایجاد' }).click();

    const row = page.locator('.MuiPaper-root', { hasText: categoryName }).first();
    await row.getByRole('button', { name: 'دسترسی‌ها' }).click();
    const acl = page.getByRole('dialog');
    await pick(page, 'دارنده', username, new RegExp(username.replace(/\./g, '\\.')));
    await acl.getByRole('button', { name: 'افزودن دسترسی' }).last().click();
    await expect(acl.getByText(`کاربر: خواننده ${username}`)).toBeVisible();

    await acl.getByRole('tab', { name: 'چرا؟' }).click();
    await pick(page, 'کاربران', username, new RegExp(username.replace(/\./g, '\\.')));
    const view = acl.locator('.MuiPaper-root', { hasText: 'دیدن سند' }).first();
    await expect(view.getByText('مجاز', { exact: true })).toBeVisible();
    await expect(view.getByText('اجازه‌ی مستقیم')).toBeVisible();
    await expect(view.getByText(`«خواننده ${username}»`)).toBeVisible();
  });

  test('a role gets a system permission and a holder', async ({ page }) => {
    const token = await adminToken();
    const username = unique('e2e.auditor.');
    await api('/api/v1/admin/users', { username, displayName: `حسابرس ${username}`, email: null, password: 'AuditorPassword!123', isSystemAdmin: false, mustChangePassword: false }, token);
    const roleName = `نقش ${unique('')}`;

    await signIn(page, 'admin', adminPassword);
    await openAdmin(page, 'نقش‌ها');
    await page.getByRole('button', { name: 'نقش جدید' }).click();
    await page.getByRole('dialog').getByLabel('کد').fill(unique('R').toUpperCase().slice(0, 20));
    await page.getByRole('dialog').getByLabel('نام', { exact: true }).fill(roleName);
    await page.getByRole('dialog').getByRole('button', { name: 'ایجاد' }).click();

    const dialog = page.getByRole('dialog');
    await dialog.getByRole('checkbox', { name: /دیدن رویدادنگاری/ }).check();
    await dialog.getByRole('button', { name: 'ذخیره', exact: true }).click();
    await expect(dialog.getByText('ذخیره شد.')).toBeVisible();

    await pick(page, 'افزودن کاربر', username, new RegExp(username.replace(/\./g, '\\.')));
    await dialog.getByRole('button', { name: 'افزودن', exact: true }).click();
    await expect(dialog.getByText(`حسابرس ${username}`)).toBeVisible();

    // The holder now sees the audit log in their menu.
    await dialog.getByRole('button', { name: 'بستن' }).click();
    const roles = await api(`/api/v1/admin/roles?userId=${(await api('/api/v1/admin/users?search=' + encodeURIComponent(username), undefined, token))[0].id}`, undefined, token);
    expect(roles.map((role: { name: string }) => role.name)).toContain(roleName);
  });
});
