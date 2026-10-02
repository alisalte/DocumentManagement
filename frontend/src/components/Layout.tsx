import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useSearchParams } from 'react-router';
import { api, type CategoryNode } from '../lib/api';
import { useSession } from '../session';
import { t } from '../strings';
import { a as audit } from '../pages/admin/auditStrings';
import { d as directory } from '../pages/admin/directoryStrings';
import { d as disposition } from '../pages/admin/dispositionStrings';
import { i as importStrings } from '../pages/admin/importStrings';
import { CategoryTree } from './CategoryTree';
import { NotificationBell } from './notifications/NotificationBell';
import { s as sharing } from './sharing/sharingStrings';
import { w } from './workflow/workflowStrings';
import { formatBytes, formatNumber } from '../lib/format';
import { applyTheme, type Theme } from '../theme';
import { Menu, MenuItem, Switch, cx, menuItemClasses } from './ui';

const sidebarWidth = 280;

function BrandMark({ className }: { className?: string }) {
  return (
    <span
      className={cx(
        'relative grid size-9 shrink-0 place-items-center overflow-hidden rounded-xl shadow-[0_2px_12px_rgb(124_58_237/0.4)]',
        className,
      )}
    >
      <span className="absolute inset-0 fillo-gradient" aria-hidden />
      <span className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgb(255_255_255/0.35),transparent_55%)]" aria-hidden />
      <svg viewBox="0 0 24 24" className="relative size-5 text-white" fill="currentColor" aria-hidden>
        <path d="M4 6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6z" opacity="0.9" />
      </svg>
    </span>
  );
}

function NavIcon({ children }: { children: ReactNode }) {
  return (
    <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-paper-300/40 text-paper-600 [&_svg]:size-4">
      {children}
    </span>
  );
}

function pageTitle(pathname: string, categoryName?: string | null): string {
  if (pathname === '/') return categoryName ?? 'مدیریت خودکار';
  if (pathname === '/new') return t.newDocument;
  if (pathname === '/tasks') return w.inbox;
  if (pathname === '/shared') return sharing.sharedWithMe;
  if (pathname === '/recycle-bin') return t.recycleBin;
  if (pathname === '/search') return t.searchEverything;
  if (pathname === '/help') return t.userGuide;
  if (pathname.startsWith('/documents/')) return t.browse;
  if (pathname.startsWith('/settings/users')) return directory.users;
  if (pathname.startsWith('/settings/groups')) return directory.groups;
  if (pathname.startsWith('/settings/roles')) return directory.roles;
  if (pathname.startsWith('/settings/categories')) return directory.categories;
  if (pathname.startsWith('/settings/document-types')) return t.documentTypes;
  if (pathname.startsWith('/settings/workflows')) return w.workflows;
  if (pathname.startsWith('/settings/search')) return t.searchAdmin;
  if (pathname.startsWith('/settings/audit')) return audit.menu;
  if (pathname.startsWith('/settings/disposition')) return disposition.menu;
  if (pathname.startsWith('/settings/imports')) return importStrings.menu;
  if (pathname.startsWith('/settings')) return 'تنظیمات';
  if (pathname.startsWith('/account/')) return directory.changePassword;
  return t.appTitle;
}

function SidebarNav({
  pending,
  onNavigate,
  pathname,
  autoManageActive,
  newDocumentHref,
}: {
  pending: number;
  onNavigate?: () => void;
  pathname: string;
  autoManageActive: boolean;
  newDocumentHref: string;
}) {
  const has = (path: string) =>
    path === '/' ? autoManageActive : pathname === path || pathname.startsWith(`${path}/`);
  const item = (active: boolean) =>
    cx(
      'flex w-full items-center gap-3 rounded-xl px-2.5 py-2 text-sm font-medium transition-colors',
      active
        ? 'bg-ink-500/15 text-ink-800 shadow-[inset_0_0_0_1px_rgb(139_92_246/0.25)]'
        : 'text-paper-600 hover:bg-paper-300/40 hover:text-ink-900',
    );

  const link = (to: string, label: ReactNode, active: boolean, icon: ReactNode, badge?: number) => (
    <RouterLink to={to} onClick={onNavigate} className={item(active)}>
      <NavIcon>{icon}</NavIcon>
      <span className="min-w-0 flex-1 truncate">{label}</span>
      {badge != null && badge > 0 && (
        <span className="rounded-md bg-paper-300/80 px-1.5 py-0.5 text-[10px] font-semibold text-paper-600">
          {badge > 99 ? '99+' : badge}
        </span>
      )}
    </RouterLink>
  );

  const gridIcon = (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" d="M4 5h6v6H4V5Zm10 0h6v6h-6V5ZM4 13h6v6H4v-6Zm10 0h6v6h-6v-6Z" />
    </svg>
  );
  const boltIcon = (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M13 2 3 14h8l-1 8 10-12h-8l1-8Z" />
    </svg>
  );
  const folderIcon = (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 7a2 2 0 0 1 2-2h5l2 2h9a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z" />
    </svg>
  );
  const taskIcon = (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h3.75M9 15h3.75M9 18h3.75m3 .75H18a2.25 2.25 0 0 0 2.25-2.25V6.108c0-1.135-.845-2.098-1.976-2.192a48.424 48.424 0 0 0-1.123-.08m-5.801 0c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 0 0 .75-.75 2.25 2.25 0 0 0-.1-.664m-5.8 0A2.251 2.251 0 0 1 13.5 2.25H15c1.012 0 1.867.668 2.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m0 0H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V9.375c0-.621-.504-1.125-1.125-1.125H8.25Z" />
    </svg>
  );
  return (
    <nav className="space-y-6">
      <div>
        <p className="section-label pb-2">منوی اصلی</p>
        <div className="space-y-0.5">
          {link('/', 'مدیریت خودکار', has('/'), boltIcon)}
          {link('/search', t.searchEverything, has('/search'), gridIcon)}
          {link(newDocumentHref, t.newDocument, has('/new'), folderIcon)}
          {link('/tasks', w.inbox, has('/tasks'), taskIcon, pending)}
        </div>
      </div>

      <div>
        <p className="section-label pb-2">اشتراک‌گذاری</p>
        <div className="space-y-0.5">
          {link('/shared', sharing.sharedWithMe, has('/shared'), folderIcon)}
          {link('/recycle-bin', t.recycleBin, has('/recycle-bin'), folderIcon)}
        </div>
      </div>
    </nav>
  );
}

function StorageWidget({ demo }: { demo?: boolean }) {
  const usage = useQuery({
    queryKey: ['storage-usage'],
    queryFn: api.storageUsage,
    refetchInterval: 60_000,
    enabled: !demo,
    retry: demo ? false : 1,
  });

  const usedBytes = demo ? 0 : (usage.data?.usedBytes ?? 0);
  const quotaBytes = demo ? 100 * 1024 * 1024 * 1024 : (usage.data?.quotaBytes ?? 0);
  const pct =
    quotaBytes > 0 ? Math.min(100, Math.round((usedBytes / quotaBytes) * 1000) / 10) : 0;
  const loading = !demo && usage.isLoading && !usage.data;

  return (
    <div className="rounded-2xl border border-paper-200 bg-paper-50 p-3.5">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-medium text-paper-600">فضای ذخیره‌سازی</p>
        {quotaBytes > 0 && !loading && !usage.isError && (
          <span className="text-[10px] text-paper-500">{formatNumber(pct)}٪</span>
        )}
      </div>
      <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-paper-200">
        <div
          className="h-full rounded-full bg-gradient-to-l from-ink-500 to-copper-500 transition-[width] duration-300"
          style={{
            width: loading || usage.isError ? '0%' : `${quotaBytes > 0 ? pct : usedBytes > 0 ? 100 : 0}%`,
          }}
        />
      </div>
      <p className="mt-2 text-xs text-paper-500">
        {demo ? (
          <>
            <span className="font-semibold text-ink-900">{formatBytes(0)}</span>
            {' از '}
            {formatBytes(quotaBytes)}
          </>
        ) : usage.isError ? (
          <span className="text-rose-600">خواندن فضا ممکن نشد</span>
        ) : loading ? (
          'در حال خواندن…'
        ) : quotaBytes > 0 ? (
          <>
            <span className="font-semibold text-ink-900">{formatBytes(usedBytes)}</span>
            {' از '}
            {formatBytes(quotaBytes)}
          </>
        ) : (
          <>
            <span className="font-semibold text-ink-900">{formatBytes(usedBytes)}</span>
            {' مصرف‌شده'}
          </>
        )}
      </p>
    </div>
  );
}

function SidebarContent({
  pending,
  searchText,
  setSearchText,
  submitSearch,
  onNavigate,
  pathname,
  autoManageActive,
  selectedCategory,
  onSelectCategory,
  categories,
  showFolderTree,
  newDocumentHref,
  demo,
  theme,
  onThemeChange,
  themeInputId,
}: {
  pending: number;
  searchText: string;
  setSearchText: (v: string) => void;
  submitSearch: (e: FormEvent) => void;
  onNavigate?: () => void;
  pathname: string;
  autoManageActive: boolean;
  selectedCategory: string | null;
  onSelectCategory: (categoryId: string | null) => void;
  categories: CategoryNode[];
  showFolderTree: boolean;
  newDocumentHref: string;
  demo?: boolean;
  theme: Theme;
  onThemeChange: (theme: Theme) => void;
  themeInputId: string;
}) {
  const settingsActive = pathname === '/settings' || pathname.startsWith('/settings/');
  return (
    <div className="flex h-full flex-col gap-4 p-4">
      <RouterLink to="/" onClick={onNavigate} className="flex items-center gap-2.5 px-1">
        <BrandMark />
        <span className="text-lg font-bold tracking-tight text-ink-900">Fillo</span>
      </RouterLink>

      <form onSubmit={submitSearch} role="search">
        <div className="relative">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            aria-hidden
            className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-paper-500"
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-4.35-4.35M17 10.5a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0Z" />
          </svg>
          <input
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            placeholder="جستجو…"
            aria-label={t.searchEverything}
            className="h-10 w-full rounded-xl border border-paper-200 bg-paper-100 ps-9 pe-14 text-sm text-ink-900 placeholder:text-paper-500 focus:border-ink-400 focus:bg-paper-50 focus:outline-none focus:ring-2 focus:ring-ink-500/15"
          />
          <kbd className="pointer-events-none absolute inset-y-0 end-2 my-auto hidden h-6 items-center rounded-md border border-paper-400/50 bg-paper-200/80 px-1.5 text-[10px] text-paper-500 sm:flex">
            ⌘K
          </kbd>
        </div>
      </form>

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto">
        <SidebarNav
          pending={pending}
          onNavigate={onNavigate}
          pathname={pathname}
          autoManageActive={autoManageActive}
          newDocumentHref={newDocumentHref}
        />

        {showFolderTree && (
          <div className="rounded-2xl border border-paper-200 bg-paper-50 p-3">
            <p className="section-label pb-2.5">{t.categories}</p>
            <CategoryTree
              categories={categories}
              selectedId={selectedCategory}
              onSelect={onSelectCategory}
            />
          </div>
        )}

      </div>

      <StorageWidget demo={demo} />

      <div className="space-y-2 border-t border-paper-300/50 pt-3">
        <RouterLink
          to="/settings"
          onClick={onNavigate}
          className={cx(
            'flex items-center gap-2 rounded-xl px-2 py-1.5 text-sm',
            settingsActive
              ? 'bg-ink-500/15 font-medium text-ink-800'
              : 'text-paper-600 hover:bg-paper-300/40 hover:text-ink-900',
          )}
        >
          <NavIcon>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6.932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-.128.332-.183.582-.495.644-.869l.214-1.28Z" />
              <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
            </svg>
          </NavIcon>
          تنظیمات
        </RouterLink>
        <div className="flex items-center justify-between rounded-xl px-2 py-1.5">
          <label htmlFor={themeInputId} className="cursor-pointer text-sm text-paper-600">
            حالت روشن
          </label>
          <Switch
            id={themeInputId}
            checked={theme === 'light'}
            onChange={(event) => onThemeChange(event.target.checked ? 'light' : 'dark')}
            aria-label="حالت روشن"
          />
        </div>
      </div>
    </div>
  );
}

/**
 * Fillo-style app frame: fixed sidebar, top bar in the content column, dark surfaces.
 */
export function Layout({ children }: { children: ReactNode }) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const { user, signOut } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const [accountMenu, setAccountMenu] = useState<HTMLElement | null>(null);
  const autoManageActive =
    location.pathname === '/' && !searchParams.get('category') && !searchParams.get('q');
  const [searchText, setSearchText] = useState('');
  const [theme, setTheme] = useState<Theme>(() =>
    document.documentElement.classList.contains('dark') ? 'dark' : 'light',
  );

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    setDrawerOpen(false);
    navigate(searchText.trim() ? `/search?q=${encodeURIComponent(searchText.trim())}` : '/search');
  };

  const tasks = useQuery({ queryKey: ['tasks'], queryFn: api.workflow.tasks, refetchInterval: 60_000 });
  const pending = tasks.data?.length ?? 0;
  const categoryId = searchParams.get('category');
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const isDemo = import.meta.env.DEV && searchParams.get('demo') === '1';
  const demoCategories: CategoryNode[] = [
    { id: 'root', parentId: null, name: 'اسناد', code: 'ROOT', description: 'ریشه‌ی بایگانی', depth: 0, isActive: true, sortOrder: 0, canView: true, canCreate: false },
    { id: 'd1', parentId: 'root', name: 'ویدیوهای پژوهش کاربر', code: 'ur', description: null, depth: 1, isActive: true, sortOrder: 1, canView: true, canCreate: true },
    { id: 'd1a', parentId: 'd1', name: 'مصاحبه‌ها', code: 'ur-i', description: null, depth: 2, isActive: true, sortOrder: 1, canView: true, canCreate: true },
    { id: 'd2', parentId: 'root', name: 'کتابخانه کامپوننت UI', code: 'ui', description: null, depth: 1, isActive: true, sortOrder: 2, canView: true, canCreate: true },
    { id: 'd3', parentId: 'root', name: 'دارایی‌های برند', code: 'br', description: null, depth: 1, isActive: true, sortOrder: 3, canView: true, canCreate: true },
    { id: 'd4', parentId: 'root', name: 'مستندات محصول', code: 'pd', description: null, depth: 1, isActive: true, sortOrder: 4, canView: true, canCreate: true },
  ];
  const folderCategories =
    categories.data && categories.data.length > 0 ? categories.data : isDemo ? demoCategories : [];
  const categoryName =
    folderCategories.find((c) => c.id === categoryId)?.name ?? null;
  const title = pageTitle(location.pathname, categoryName);
  const showFolderTree =
    location.pathname !== '/help' &&
    !location.pathname.startsWith('/help/') &&
    !location.pathname.startsWith('/settings');

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);
  const filingCategoryId =
    (categoryId && folderCategories.some((c) => c.id === categoryId && c.canCreate)
      ? categoryId
      : null) ??
    folderCategories.find((c) => c.canCreate)?.id ??
    null;
  const newDocumentHref = filingCategoryId ? `/new?category=${filingCategoryId}` : '/new';
  const selectCategory = (id: string | null) => {
    setDrawerOpen(false);
    if (!id) {
      navigate(isDemo ? '/?demo=1' : '/');
      return;
    }
    navigate(isDemo ? `/?category=${id}&demo=1` : `/?category=${id}`);
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        navigate('/search');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [navigate]);

  const topBar = (
    <header className="flex h-16 shrink-0 items-center gap-3 border-b border-paper-200 bg-paper-50/70 px-4 sm:px-6 backdrop-blur-md">
      <button
        type="button"
        onClick={() => setDrawerOpen(true)}
        aria-label={t.menu}
        className="inline-flex size-9 shrink-0 items-center justify-center rounded-xl text-paper-500 hover:bg-paper-300/50 lg:hidden"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} aria-hidden className="size-5">
          <path strokeLinecap="round" d="M4 6h16M4 12h16M4 18h16" />
        </svg>
      </button>

      <div className="flex min-w-0 items-center gap-2">
        <span className="grid size-9 place-items-center rounded-xl bg-ink-500/15 text-ink-600">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="size-5">
            <path strokeLinecap="round" strokeLinejoin="round" d="M13 2 3 14h8l-1 8 10-12h-8l1-8Z" />
          </svg>
        </span>
        <h1 className="truncate text-lg font-semibold text-ink-900">{title}</h1>
      </div>

      <div className="ms-auto flex shrink-0 items-center gap-1 sm:gap-2">
        <RouterLink
          to="/help"
          aria-label={t.userGuide}
          className="inline-flex size-9 items-center justify-center rounded-xl text-paper-500 hover:bg-paper-300/50 hover:text-ink-900"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden className="size-5">
            <path strokeLinecap="round" strokeLinejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 5.25h.008v.008H12v-.008Z" />
          </svg>
        </RouterLink>

        <NotificationBell />

        <button
          type="button"
          onClick={(e) => setAccountMenu(e.currentTarget)}
          aria-haspopup="menu"
          className="flex max-w-[11rem] items-center gap-2 rounded-xl border border-paper-200 bg-paper-50 py-1.5 ps-1.5 pe-2.5 text-sm hover:bg-ink-50"
        >
          <span className="grid size-8 shrink-0 place-items-center rounded-lg fillo-gradient text-xs font-bold text-white">
            {(user?.displayName ?? '?').slice(0, 1)}
          </span>
          <span className="hidden truncate font-medium text-ink-900 sm:inline">{user?.displayName}</span>
          <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-4 shrink-0 text-paper-500">
            <path d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z" />
          </svg>
        </button>
        <Menu anchor={accountMenu} onClose={() => setAccountMenu(null)}>
          <div className="border-b border-paper-300/60 px-3 py-2.5">
            <p className="truncate text-sm font-medium text-ink-900">{user?.displayName}</p>
            <p className="truncate text-xs text-paper-500" dir="ltr">
              {user?.username}
            </p>
          </div>
          <RouterLink to="/settings" onClick={() => setAccountMenu(null)} className={menuItemClasses('mt-0.5')}>
            تنظیمات
          </RouterLink>
          <RouterLink to="/help" onClick={() => setAccountMenu(null)} className={menuItemClasses()}>
            {t.userGuide}
          </RouterLink>
          <RouterLink to="/account/password" onClick={() => setAccountMenu(null)} className={menuItemClasses()}>
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
    </header>
  );

  return (
    <div className="flex min-h-screen">
      <aside
        className="sticky top-0 hidden h-screen shrink-0 overflow-hidden border-e border-paper-200 bg-paper-50/80 lg:block"
        style={{ width: sidebarWidth }}
      >
        <SidebarContent
          pending={pending}
          searchText={searchText}
          setSearchText={setSearchText}
          submitSearch={submitSearch}
          pathname={location.pathname}
          autoManageActive={autoManageActive}
          selectedCategory={categoryId}
          onSelectCategory={selectCategory}
          categories={folderCategories}
          showFolderTree={showFolderTree}
          newDocumentHref={newDocumentHref}
          demo={isDemo}
          theme={theme}
          onThemeChange={setTheme}
          themeInputId="theme-light"
        />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {topBar}
        <main className="page-enter min-h-0 flex-1 overflow-auto p-4 sm:p-6">{children}</main>
      </div>

      {drawerOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" onClick={() => setDrawerOpen(false)} aria-hidden />
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t.menu}
            className="absolute inset-y-0 start-0 flex w-[85vw] max-w-xs flex-col overflow-hidden bg-paper-50 shadow-[0_16px_40px_rgb(31_22_56/0.18)] animate-[slide-in_0.28s_ease-out]"
            style={{ maxWidth: sidebarWidth }}
          >
            <SidebarContent
              pending={pending}
              searchText={searchText}
              setSearchText={setSearchText}
              submitSearch={submitSearch}
              onNavigate={() => setDrawerOpen(false)}
              pathname={location.pathname}
              autoManageActive={autoManageActive}
              selectedCategory={categoryId}
              onSelectCategory={selectCategory}
              categories={folderCategories}
              showFolderTree={showFolderTree}
              newDocumentHref={newDocumentHref}
              demo={isDemo}
              theme={theme}
              onThemeChange={setTheme}
              themeInputId="theme-light-drawer"
            />
          </div>
        </div>
      )}
    </div>
  );
}
