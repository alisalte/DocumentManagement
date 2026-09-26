import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent, type ReactNode } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useSearchParams } from 'react-router';
import { api } from '../lib/api';
import { useSession } from '../session';
import { t } from '../strings';
import { a as audit } from '../pages/admin/auditStrings';
import { d as directory } from '../pages/admin/directoryStrings';
import { CategoryTree } from './CategoryTree';
import { NotificationBell } from './notifications/NotificationBell';
import { s as sharing } from './sharing/sharingStrings';
import { w } from './workflow/workflowStrings';
import { Badge, Button, Menu, MenuItem, cx, menuItemClasses } from './ui';

const drawerWidth = 288;

function BrandMark({ className }: { className?: string }) {
  return (
    <span
      className={cx(
        'relative grid size-9 shrink-0 place-items-center overflow-hidden rounded-xl bg-ink-800 text-base font-bold text-white shadow-[0_2px_8px_rgb(12_32_52/0.25)]',
        className,
      )}
    >
      <span className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgb(255_255_255/0.18),transparent_55%)]" aria-hidden />
      <span className="relative">ب</span>
    </span>
  );
}

/**
 * App frame. On a desktop the category tree is a permanent side panel; on a phone it is a
 * drawer behind a button, so the document list gets the full width.
 */
export function Layout({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const { user, signOut } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const selectedCategory = params.get('category');
  const has = (code: string) => !!user && (user.isSystemAdmin || user.systemPermissions.includes(code));
  // Only screens the server would let this user use; it still checks every call.
  const adminLinks = [
    { to: '/admin/users', label: directory.users, allowed: has('ADMIN_MANAGE_USERS') },
    { to: '/admin/groups', label: directory.groups, allowed: has('ADMIN_MANAGE_GROUPS') },
    { to: '/admin/roles', label: directory.roles, allowed: has('ADMIN_MANAGE_ROLES') },
    { to: '/admin/categories', label: directory.categories, allowed: has('ADMIN_MANAGE_CATEGORIES') },
    { to: '/admin/document-types', label: t.documentTypes, allowed: has('ADMIN_MANAGE_DOCUMENT_TYPES') },
    { to: '/admin/workflows', label: w.workflows, allowed: has('ADMIN_MANAGE_WORKFLOWS') },
    { to: '/admin/search', label: t.searchAdmin, allowed: has('ADMIN_MANAGE_SEARCH') },
    { to: '/admin/audit', label: audit.menu, allowed: has('AUDIT_VIEW') },
  ].filter((link) => link.allowed);
  const [adminMenu, setAdminMenu] = useState<HTMLElement | null>(null);
  const [accountMenu, setAccountMenu] = useState<HTMLElement | null>(null);
  const [searchText, setSearchText] = useState('');

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    setOpen(false);
    navigate(searchText.trim() ? `/search?q=${encodeURIComponent(searchText.trim())}` : '/search');
  };
  const tasks = useQuery({ queryKey: ['tasks'], queryFn: api.workflow.tasks, refetchInterval: 60_000 });
  const pending = tasks.data?.length ?? 0;

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });

  const selectCategory = (categoryId: string | null) => {
    setOpen(false);
    navigate(categoryId ? `/?category=${categoryId}` : '/');
  };

  const isActive = (path: string) => location.pathname === path;
  const navLink = (active: boolean) =>
    cx(
      'inline-flex h-9 items-center rounded-xl px-3 text-sm font-medium transition-all duration-150',
      'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
      active
        ? 'bg-ink-100/90 text-ink-800 shadow-[inset_0_0_0_1px_rgb(30_74_117/0.12)]'
        : 'text-paper-600 hover:bg-ink-50 hover:text-ink-900',
    );

  const folderTree = (
    <div>
      <p className="section-label pb-2.5">{t.categories}</p>
      <CategoryTree categories={categories.data ?? []} selectedId={selectedCategory} onSelect={selectCategory} />
    </div>
  );

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-40 border-b border-paper-200/80 bg-white/80 backdrop-blur-md">
        <div className="flex h-[4.25rem] items-center gap-2 px-3 sm:px-5">
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label={t.menu}
            className="inline-flex size-9 shrink-0 items-center justify-center rounded-xl text-paper-600 hover:bg-ink-50 lg:hidden"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden className="size-5">
              <path strokeLinecap="round" d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>

          <RouterLink to="/" className="flex min-w-0 items-center gap-2.5">
            <BrandMark />
            <span className="truncate text-base font-bold tracking-tight text-ink-900 sm:text-lg">{t.appTitle}</span>
          </RouterLink>

          <form onSubmit={submitSearch} role="search" className="mx-auto hidden w-full max-w-md sm:block">
            <div className="relative">
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={2}
                aria-hidden
                className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-paper-400"
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-4.35-4.35M17 10.5a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0Z" />
              </svg>
              <input
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={t.searchEverything}
                aria-label={t.searchEverything}
                enterKeyHint="search"
                className="h-10 w-full rounded-xl border border-transparent bg-paper-100/90 ps-9 pe-3 text-sm text-ink-900 transition-all placeholder:text-paper-500 focus:border-ink-400 focus:bg-white focus:outline-none focus:ring-2 focus:ring-ink-500/15"
              />
            </div>
          </form>

          <div className="ms-auto flex shrink-0 items-center gap-0.5 sm:gap-1">
            <RouterLink
              to="/search"
              aria-label={t.searchEverything}
              className="inline-flex size-9 items-center justify-center rounded-xl text-paper-600 hover:bg-ink-50 sm:hidden"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden className="size-5">
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-4.35-4.35M17 10.5a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0Z" />
              </svg>
            </RouterLink>

            <NotificationBell />

            <RouterLink
              to="/tasks"
              className={cx(navLink(isActive('/tasks')), 'h-9 gap-1.5 px-2 sm:px-3')}
              aria-label={w.inbox}
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden className="size-5">
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h3.75M9 15h3.75M9 18h3.75m3 .75H18a2.25 2.25 0 0 0 2.25-2.25V6.108c0-1.135-.845-2.098-1.976-2.192a48.424 48.424 0 0 0-1.123-.08m-5.801 0c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 0 0 .75-.75 2.25 2.25 0 0 0-.1-.664m-5.8 0A2.251 2.251 0 0 1 13.5 2.25H15c1.012 0 1.867.668 2.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m0 0H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V9.375c0-.621-.504-1.125-1.125-1.125H8.25Z" />
              </svg>
              <span className="hidden sm:inline">{w.inbox}</span>
              <Badge count={pending} max={99} className="[&_span]:text-[10px]" />
            </RouterLink>

            <RouterLink to="/shared" className={cx(navLink(isActive('/shared')), 'hidden lg:inline-flex')}>
              {sharing.sharedWithMe}
            </RouterLink>
            <RouterLink to="/recycle-bin" className={cx(navLink(isActive('/recycle-bin')), 'hidden lg:inline-flex')}>
              {t.recycleBin}
            </RouterLink>

            {adminLinks.length > 0 && (
              <div className="hidden lg:block">
                <button
                  type="button"
                  onClick={(event) => setAdminMenu(event.currentTarget)}
                  aria-haspopup="menu"
                  className={cx(navLink(false), 'gap-1')}
                >
                  {directory.administration}
                  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5 opacity-60">
                    <path d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z" />
                  </svg>
                </button>
                <Menu anchor={adminMenu} onClose={() => setAdminMenu(null)}>
                  {adminLinks.map((link) => (
                    <RouterLink
                      key={link.to}
                      to={link.to}
                      role="menuitem"
                      onClick={() => setAdminMenu(null)}
                      className={menuItemClasses()}
                    >
                      {link.label}
                    </RouterLink>
                  ))}
                </Menu>
              </div>
            )}

            <RouterLink
              to="/new"
              className={cx(
                'hidden h-9 items-center rounded-xl bg-ink-700 px-3.5 text-sm font-medium text-white shadow-[0_1px_2px_rgb(12_32_52/0.15)] transition-colors hover:bg-ink-800 lg:inline-flex',
              )}
            >
              {t.newDocument}
            </RouterLink>

            <div className="hidden lg:block">
              <button
                type="button"
                onClick={(event) => setAccountMenu(event.currentTarget)}
                aria-haspopup="menu"
                className={cx(navLink(false), 'max-w-40 gap-1.5')}
              >
                <span className="truncate">{user?.displayName}</span>
                <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5 shrink-0 opacity-60">
                  <path d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z" />
                </svg>
              </button>
              <Menu anchor={accountMenu} onClose={() => setAccountMenu(null)}>
                <div className="border-b border-paper-100 px-3 py-2.5">
                  <p className="truncate text-sm font-medium text-ink-900">{user?.displayName}</p>
                  <p className="truncate text-xs text-paper-400" dir="ltr">
                    {user?.username}
                  </p>
                </div>
                <RouterLink
                  to="/account/password"
                  onClick={() => setAccountMenu(null)}
                  className={menuItemClasses('mt-0.5')}
                >
                  {directory.changePassword}
                </RouterLink>
                <MenuItem
                  danger
                  onClick={() => {
                    setAccountMenu(null);
                    void signOut();
                  }}
                >
                  {t.signOut}
                </MenuItem>
              </Menu>
            </div>
          </div>
        </div>
      </header>

      <div className="flex flex-1">
        <aside
          className="sticky top-[4.25rem] hidden h-[calc(100vh-4.25rem)] w-72 shrink-0 overflow-y-auto border-e border-paper-200/80 bg-white/55 p-4 backdrop-blur-sm lg:block"
          style={{ width: drawerWidth }}
        >
          {folderTree}
        </aside>

        <main className="page-enter min-w-0 flex-1 px-3 py-4 sm:px-6 sm:py-7">{children}</main>
      </div>

      {/* On a phone the bar keeps only notifications and tasks; the rest lives in the drawer. */}
      {open && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div className="absolute inset-0 bg-ink-950/45 backdrop-blur-[2px]" onClick={() => setOpen(false)} aria-hidden />
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t.menu}
            className="absolute inset-y-0 start-0 flex w-[85vw] max-w-xs flex-col overflow-y-auto bg-white shadow-[0_16px_40px_rgb(12_32_52/0.18)] animate-[slide-in_0.28s_ease-out]"
            style={{ maxWidth: drawerWidth }}
          >
            <div className="flex h-[4.25rem] shrink-0 items-center justify-between gap-2 border-b border-paper-200 px-4">
              <div className="flex min-w-0 items-center gap-2.5">
                <BrandMark className="size-8 text-sm" />
                <span className="truncate text-base font-bold text-ink-900">{t.appTitle}</span>
              </div>
              <button
                type="button"
                onClick={() => setOpen(false)}
                aria-label="بستن"
                className="size-8 rounded-xl text-paper-400 hover:bg-ink-50 hover:text-ink-800"
              >
                <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="mx-auto size-4">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
                </svg>
              </button>
            </div>

            <div className="space-y-4 p-4">
              <Button fullWidth as={RouterLink} to="/new" onClick={() => setOpen(false)}>
                {t.newDocument}
              </Button>

              <nav className="space-y-0.5">
                <RouterLink to="/tasks" onClick={() => setOpen(false)} className={cx(navLink(isActive('/tasks')), 'w-full justify-start')}>
                  {w.inbox}
                </RouterLink>
                <RouterLink
                  to="/shared"
                  onClick={() => setOpen(false)}
                  className={cx(navLink(isActive('/shared')), 'w-full justify-start')}
                >
                  {sharing.sharedWithMe}
                </RouterLink>
                <RouterLink
                  to="/recycle-bin"
                  onClick={() => setOpen(false)}
                  className={cx(navLink(isActive('/recycle-bin')), 'w-full justify-start')}
                >
                  {t.recycleBin}
                </RouterLink>
              </nav>

              {adminLinks.length > 0 && (
                <div className="border-t border-paper-100 pt-4">
                  <p className="section-label pb-2">{directory.administration}</p>
                  <nav className="space-y-0.5">
                    {adminLinks.map((link) => (
                      <RouterLink
                        key={link.to}
                        to={link.to}
                        onClick={() => setOpen(false)}
                        className={cx(navLink(isActive(link.to)), 'w-full justify-start')}
                      >
                        {link.label}
                      </RouterLink>
                    ))}
                  </nav>
                </div>
              )}

              <div className="border-t border-paper-100 pt-4">{folderTree}</div>

              <div className="space-y-0.5 border-t border-paper-100 pt-4">
                <p className="section-label pb-2">{user?.displayName}</p>
                <RouterLink
                  to="/account/password"
                  onClick={() => setOpen(false)}
                  className={cx(navLink(false), 'w-full justify-start')}
                >
                  {directory.changePassword}
                </RouterLink>
                <button
                  type="button"
                  onClick={() => {
                    setOpen(false);
                    void signOut();
                  }}
                  className={cx(navLink(false), 'w-full justify-start text-rose-600 hover:bg-rose-50 hover:text-rose-700')}
                >
                  {t.signOut}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
