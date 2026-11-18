import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useSearchParams } from 'react-router';
import { api, type CategoryNode } from '../lib/api';
import { subtreeFileCounts } from '../lib/categories';
import { formatFiscalYear, useFiscalYear } from '../lib/fiscalYear';
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
import {
  CalendarIcon,
  ChevronDownIcon,
  CloseIcon,
  HelpIcon,
  HomeIcon,
  InboxIcon,
  KeyIcon,
  LogoutIcon,
  MenuIcon,
  MoonIcon,
  PlusIcon,
  SearchIcon,
  SettingsIcon,
  ShareIcon,
  SunIcon,
  TrashIcon,
} from './icons';

const sidebarWidth = 288;

const demoFolders: CategoryNode[] = [
  { id: 'root', parentId: null, name: 'اسناد', code: 'ROOT', description: 'ریشه‌ی بایگانی', depth: 0, isActive: true, sortOrder: 0, canView: true, canCreate: false },
  { id: 'd1', parentId: 'root', name: 'ویدیوهای پژوهش کاربر', code: 'ur', description: null, depth: 1, isActive: true, sortOrder: 1, canView: true, canCreate: true },
  { id: 'd1a', parentId: 'd1', name: 'مصاحبه‌ها', code: 'ur-i', description: null, depth: 2, isActive: true, sortOrder: 1, canView: true, canCreate: true },
  { id: 'd2', parentId: 'root', name: 'کتابخانه کامپوننت UI', code: 'ui', description: null, depth: 1, isActive: true, sortOrder: 2, canView: true, canCreate: true },
  { id: 'd3', parentId: 'root', name: 'دارایی‌های برند', code: 'br', description: null, depth: 1, isActive: true, sortOrder: 3, canView: true, canCreate: true },
  { id: 'd4', parentId: 'root', name: 'مستندات محصول', code: 'pd', description: null, depth: 1, isActive: true, sortOrder: 4, canView: true, canCreate: true },
];

function BrandMark({ className }: { className?: string }) {
  return (
    <span
      className={cx(
        'relative grid size-9 shrink-0 place-items-center overflow-hidden rounded-xl fillo-gradient shadow-[var(--shadow-brand)]',
        className,
      )}
    >
      <span className="absolute inset-0 bg-[radial-gradient(circle_at_30%_15%,rgb(255_255_255/0.35),transparent_60%)]" aria-hidden />
      <svg viewBox="0 0 24 24" className="relative size-5 text-white" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden>
        <path strokeLinecap="round" strokeLinejoin="round" d="M4 7.5A2.5 2.5 0 0 1 6.5 5h3.2l2 2h5.8A2.5 2.5 0 0 1 20 9.5v7a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 16.5v-9Z" />
        <path strokeLinecap="round" d="M8.5 13h7" />
      </svg>
    </span>
  );
}

function Brand({ to }: { to: string }) {
  return (
    <RouterLink to={to} className="flex min-w-0 items-center gap-2.5 rounded-xl">
      <BrandMark />
      <span className="min-w-0 leading-tight">
        <span className="block text-[15px] font-extrabold tracking-tight text-ink-900">Fillo</span>
        <span className="block truncate text-[11px] text-paper-500">{t.appTitle}</span>
      </span>
    </RouterLink>
  );
}

function pageTitle(pathname: string, categoryName?: string | null): string {
  if (pathname === '/') return categoryName ?? 'مدیریت خودکار';
  if (pathname === '/new') return t.newDocument;
  if (pathname === '/tasks') return w.inbox;
  if (pathname === '/shared' || pathname.startsWith('/shared/')) return sharing.sharedWithMe;
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

function CountPill({ count, tone = 'neutral' }: { count: number; tone?: 'neutral' | 'brand' }) {
  if (count <= 0) return null;
  return (
    <span
      className={cx(
        'ms-auto min-w-5 rounded-full px-1.5 py-0.5 text-center text-[10px] leading-none font-bold tabular-nums',
        tone === 'brand' ? 'bg-ink-600 text-white' : 'bg-paper-200 text-paper-600',
      )}
    >
      {count > 99 ? '۹۹+' : formatNumber(count)}
    </span>
  );
}

function SidebarNav({
  pending,
  onNavigate,
  pathname,
  autoManageActive,
  homeHref,
}: {
  pending: number;
  onNavigate?: () => void;
  pathname: string;
  autoManageActive: boolean;
  homeHref: string;
}) {
  const has = (path: string) =>
    path === '/' ? autoManageActive : pathname === path || pathname.startsWith(`${path}/`);

  const link = (to: string, label: ReactNode, active: boolean, icon: ReactNode, badge?: number) => (
    <RouterLink
      to={to}
      onClick={onNavigate}
      aria-current={active ? 'page' : undefined}
      className={cx(
        'group relative flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition-colors duration-150',
        active
          ? 'bg-ink-50 text-ink-700'
          : 'text-paper-600 hover:bg-paper-100 hover:text-ink-900',
      )}
    >
      {active && <span className="absolute inset-y-2 -start-3 w-1 rounded-e-full bg-ink-600" aria-hidden />}
      <span
        className={cx(
          'shrink-0 [&_svg]:size-5',
          active ? 'text-ink-600' : 'text-paper-400 group-hover:text-paper-600',
        )}
      >
        {icon}
      </span>
      <span className="min-w-0 flex-1 truncate">{label}</span>
      {badge != null && <CountPill count={badge} tone="brand" />}
    </RouterLink>
  );

  return (
    <nav aria-label="منوی اصلی" className="space-y-0.5">
      {link(homeHref, 'مدیریت خودکار', has('/'), <HomeIcon />)}
      {link('/search', t.searchEverything, has('/search'), <SearchIcon />)}
      {link('/tasks', w.inbox, has('/tasks'), <InboxIcon />, pending)}
      {link('/shared', sharing.sharedWithMe, has('/shared'), <ShareIcon />)}
      {link('/recycle-bin', t.recycleBin, has('/recycle-bin'), <TrashIcon />)}
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
  const tone = pct >= 90 ? 'bg-rose-500' : pct >= 75 ? 'bg-amber-500' : 'bg-ink-500';

  return (
    <div className="rounded-xl border border-paper-200 bg-paper-100/60 p-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-semibold text-paper-700">فضای ذخیره‌سازی</p>
        {quotaBytes > 0 && !loading && !usage.isError && (
          <span className="text-[11px] font-medium text-paper-500 tabular-nums">{formatNumber(pct)}٪</span>
        )}
      </div>
      <div
        className="mt-2 h-1.5 overflow-hidden rounded-full bg-paper-200"
        role="progressbar"
        aria-label="فضای ذخیره‌سازی"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div
          className={cx('h-full rounded-full transition-[width] duration-500', tone)}
          style={{
            width: loading || usage.isError ? '0%' : `${quotaBytes > 0 ? pct : usedBytes > 0 ? 100 : 0}%`,
          }}
        />
      </div>
      <p className="mt-2 text-[11px] text-paper-500">
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

function ThemeToggle({ theme, onThemeChange, inputId }: { theme: Theme; onThemeChange: (theme: Theme) => void; inputId: string }) {
  return (
    <div className="flex items-center justify-between gap-2 rounded-xl px-3 py-2">
      <label htmlFor={inputId} className="flex cursor-pointer items-center gap-3 text-sm font-medium text-paper-600">
        <span className="text-paper-400 [&_svg]:size-5">{theme === 'light' ? <SunIcon /> : <MoonIcon />}</span>
        حالت روشن
      </label>
      <Switch
        id={inputId}
        checked={theme === 'light'}
        onChange={(event) => onThemeChange(event.target.checked ? 'light' : 'dark')}
        aria-label="حالت روشن"
      />
    </div>
  );
}

function SidebarContent({
  searchText,
  setSearchText,
  submitSearch,
  onNavigate,
  pathname,
  selectedCategory,
  onSelectCategory,
  categories,
  showFolderTree,
  fileCounts,
  onCreateDocument,
  demo,
  theme,
  onThemeChange,
  themeInputId,
  pending,
  autoManageActive,
  newDocumentHref,
  homeHref,
  showSearch,
  canFile,
}: {
  searchText: string;
  setSearchText: (v: string) => void;
  submitSearch: (e: FormEvent) => void;
  onNavigate?: () => void;
  pathname: string;
  selectedCategory: string | null;
  onSelectCategory: (categoryId: string | null) => void;
  categories: CategoryNode[];
  showFolderTree: boolean;
  fileCounts: Record<string, number> | null;
  onCreateDocument?: (categoryId: string) => void;
  demo?: boolean;
  theme: Theme;
  onThemeChange: (theme: Theme) => void;
  themeInputId: string;
  pending: number;
  autoManageActive: boolean;
  newDocumentHref: string;
  homeHref: string;
  /** The drawer carries the search box; on a desktop it lives in the top bar. */
  showSearch: boolean;
  canFile: boolean;
}) {
  const settingsActive = pathname === '/settings' || pathname.startsWith('/settings/');
  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex h-16 shrink-0 items-center px-4">
        <Brand to={homeHref} />
      </div>

      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-3 pb-3">
        {showSearch && (
          <form onSubmit={submitSearch} role="search">
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-paper-400" />
              <input
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                placeholder="جستجو در اسناد…"
                aria-label={t.searchEverything}
                className="h-10 w-full rounded-xl border border-paper-200 bg-paper-100 ps-9 pe-3 text-sm text-ink-900 placeholder:text-paper-400 focus:border-ink-400 focus:bg-paper-50 focus:outline-none focus:ring-4 focus:ring-ink-500/15"
              />
            </div>
          </form>
        )}

        {canFile && (
          <RouterLink
            to={newDocumentHref}
            onClick={onNavigate}
            aria-current={pathname === '/new' ? 'page' : undefined}
            className="flex h-10 items-center justify-center gap-2 rounded-xl bg-ink-600 px-4 text-sm font-semibold text-white shadow-[var(--shadow-brand)] transition-colors hover:bg-ink-700 active:scale-[0.98]"
          >
            <PlusIcon className="size-4.5" strokeWidth={2.2} />
            {t.newDocument}
          </RouterLink>
        )}

        <SidebarNav
          pending={pending}
          onNavigate={onNavigate}
          pathname={pathname}
          autoManageActive={autoManageActive}
          homeHref={homeHref}
        />

        {showFolderTree && (
          <div className="flex min-h-48 flex-1 flex-col">
            <p className="section-label flex items-center justify-between px-3 pb-2">
              <span>{t.categories}</span>
            </p>
            <div className="min-h-0 flex-1 overflow-y-auto">
              <CategoryTree
                categories={categories}
                selectedId={selectedCategory}
                onSelect={onSelectCategory}
                fileCounts={fileCounts}
                onCreateDocument={onCreateDocument}
              />
            </div>
          </div>
        )}
      </div>

      <div className="shrink-0 space-y-1 border-t border-paper-200 p-3">
        <StorageWidget demo={demo} />
        <RouterLink
          to="/settings"
          onClick={onNavigate}
          aria-current={settingsActive ? 'page' : undefined}
          className={cx(
            'mt-2 flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition-colors',
            settingsActive
              ? 'bg-ink-50 text-ink-700'
              : 'text-paper-600 hover:bg-paper-100 hover:text-ink-900',
          )}
        >
          <span className={cx('[&_svg]:size-5', settingsActive ? 'text-ink-600' : 'text-paper-400')}>
            <SettingsIcon />
          </span>
          تنظیمات
        </RouterLink>
        <ThemeToggle theme={theme} onThemeChange={onThemeChange} inputId={themeInputId} />
      </div>
    </div>
  );
}

/** Phone tab bar: the four places people go most, one thumb away. */
function BottomNav({
  pathname,
  autoManageActive,
  homeHref,
  newDocumentHref,
  pending,
  canFile,
}: {
  pathname: string;
  autoManageActive: boolean;
  homeHref: string;
  newDocumentHref: string;
  pending: number;
  canFile: boolean;
}) {
  const item = (to: string, label: string, icon: ReactNode, active: boolean, badge = 0) => (
    <RouterLink
      to={to}
      aria-current={active ? 'page' : undefined}
      className={cx(
        'relative flex flex-1 flex-col items-center gap-1 py-2 text-[11px] font-medium transition-colors',
        active ? 'text-ink-600' : 'text-paper-500 hover:text-ink-900',
      )}
    >
      <span className="relative [&_svg]:size-6">
        {icon}
        {badge > 0 && (
          <span className="absolute -top-1 -end-2 min-w-4 rounded-full bg-rose-500 px-1 text-center text-[10px] leading-4 font-bold text-white ring-2 ring-paper-50">
            {badge > 99 ? '۹۹+' : formatNumber(badge)}
          </span>
        )}
      </span>
      <span className="max-w-full truncate px-1">{label}</span>
    </RouterLink>
  );
  const is = (path: string) => pathname === path || pathname.startsWith(`${path}/`);
  return (
    <nav
      aria-label="میان‌بُرها"
      className="fixed inset-x-0 bottom-0 z-40 border-t border-paper-200 bg-paper-50/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-lg lg:hidden"
    >
      <div className="mx-auto flex max-w-lg items-stretch px-2">
        {item(homeHref, 'خانه', <HomeIcon />, autoManageActive)}
        {item('/search', 'جستجو', <SearchIcon />, is('/search'))}
        {canFile && (
          <div className="flex flex-1 items-center justify-center">
            <RouterLink
              to={newDocumentHref}
              aria-label={t.newDocument}
              className="grid size-12 -translate-y-3 place-items-center rounded-2xl bg-ink-600 text-white shadow-[var(--shadow-brand)] ring-4 ring-paper-100 transition-transform active:scale-95"
            >
              <PlusIcon className="size-6" strokeWidth={2.2} />
            </RouterLink>
          </div>
        )}
        {item('/tasks', w.inbox, <InboxIcon />, is('/tasks'), pending)}
        {item('/shared', 'اشتراک‌ها', <ShareIcon />, is('/shared'))}
      </div>
    </nav>
  );
}

/**
 * App frame: a fixed sidebar (navigation, folders, storage) on a desktop, a drawer and a bottom tab
 * bar on a phone, and a top bar with search, the fiscal year, notifications and the account menu.
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
  const topSearchRef = useRef<HTMLInputElement>(null);
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
  const folderCategories = useMemo(
    () => (categories.data && categories.data.length > 0 ? categories.data : isDemo ? demoFolders : []),
    [categories.data, isDemo],
  );
  const fiscal = useFiscalYear();
  const fiscalYear = fiscal.ready && fiscal.selectedYear > 0 ? fiscal.selectedYear : undefined;
  const categoryCounts = useQuery({
    queryKey: ['document-counts-by-category', fiscalYear ?? null],
    queryFn: () => api.documentCounts(fiscalYear),
    enabled: !isDemo && (fiscal.ready || fiscal.failed),
    staleTime: 30_000,
  });
  const fileCounts = useMemo(() => {
    if (!isDemo && !categoryCounts.isSuccess) return null;
    const direct: Record<string, number> = {};
    if (isDemo) {
      direct.d1a = 1;
      direct.d4 = 1;
    } else {
      for (const row of categoryCounts.data ?? []) direct[row.categoryId] = row.count;
    }
    return subtreeFileCounts(folderCategories, direct);
  }, [isDemo, categoryCounts.isSuccess, categoryCounts.data, folderCategories]);
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
  const createInCategory = (id: string) => {
    setDrawerOpen(false);
    navigate(isDemo ? `/new?category=${id}&demo=1` : `/new?category=${id}`);
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        const input = topSearchRef.current;
        if (input && input.offsetParent !== null) input.focus();
        else navigate('/search');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [navigate]);

  // A route change closes the drawer, whatever started it.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname, location.search]);

  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setDrawerOpen(false);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [drawerOpen]);

  const homeHref = isDemo ? '/?demo=1' : '/';
  const canFile = !fiscal.closed;

  const sidebarProps = {
    searchText,
    setSearchText,
    submitSearch,
    pathname: location.pathname,
    selectedCategory: categoryId,
    onSelectCategory: selectCategory,
    categories: folderCategories,
    showFolderTree,
    fileCounts,
    onCreateDocument: fiscal.closed ? undefined : createInCategory,
    demo: isDemo,
    theme,
    onThemeChange: setTheme,
    pending,
    autoManageActive,
    newDocumentHref,
    homeHref,
    canFile,
  };

  const iconLink =
    'inline-flex size-9 shrink-0 items-center justify-center rounded-xl text-paper-500 transition-colors hover:bg-paper-200/70 hover:text-ink-900 [&_svg]:size-5';

  const topBar = (
    <header className="sticky top-0 z-30 shrink-0 border-b border-paper-200 bg-paper-50/85 backdrop-blur-lg">
      <div className="flex h-16 items-center gap-2 px-3 sm:gap-3 sm:px-6">
        <button
          type="button"
          onClick={() => setDrawerOpen(true)}
          aria-label={t.menu}
          aria-expanded={drawerOpen}
          className={cx(iconLink, 'lg:hidden')}
        >
          <MenuIcon />
        </button>

        <div className="flex min-w-0 items-center gap-2.5 lg:hidden">
          <RouterLink to={homeHref} aria-label="Fillo" className="shrink-0">
            <BrandMark className="size-8" />
          </RouterLink>
        </div>

        <p className="min-w-0 truncate text-sm font-semibold text-ink-900 sm:text-base lg:hidden">{title}</p>

        <form onSubmit={submitSearch} role="search" className="hidden max-w-md flex-1 lg:block">
          <div className="relative">
            <SearchIcon className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-paper-400" />
            <input
              ref={topSearchRef}
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              placeholder="جستجو در عنوان، متن و فراداده‌ی اسناد…"
              aria-label={t.searchEverything}
              className="h-10 w-full rounded-xl border border-paper-200 bg-paper-100 ps-9 pe-16 text-sm text-ink-900 transition-[border-color,box-shadow,background-color] placeholder:text-paper-400 hover:border-paper-300 focus:border-ink-400 focus:bg-paper-50 focus:outline-none focus:ring-4 focus:ring-ink-500/15"
            />
            <kbd
              dir="ltr"
              className="pointer-events-none absolute inset-y-0 end-2 my-auto flex h-6 items-center gap-0.5 rounded-md border border-paper-300 bg-paper-50 px-1.5 font-sans text-[10px] font-medium text-paper-500"
            >
              Ctrl K
            </kbd>
          </div>
        </form>

        <div className="ms-auto flex shrink-0 items-center gap-1 sm:gap-1.5">
          {fiscal.years.length > 0 && (
            <label className="relative hidden items-center sm:flex">
              <CalendarIcon className="pointer-events-none absolute start-2.5 size-4 text-paper-400" />
              <select
                aria-label={t.fiscalYear}
                value={fiscal.selectedYear}
                onChange={(event) => fiscal.selectYear(Number(event.target.value))}
                className="h-9 max-w-[13rem] appearance-none truncate rounded-xl border border-paper-200 bg-paper-50 ps-8 pe-8 text-xs font-medium text-ink-900 transition-colors hover:border-paper-300 focus:border-ink-400 focus:outline-none focus:ring-4 focus:ring-ink-500/15"
              >
                {fiscal.years.map((year) => (
                  <option key={year.year} value={year.year}>
                    {formatFiscalYear(year.year)}
                    {' · '}
                    {year.status === 'Open' ? t.fiscalYearOpen : t.fiscalYearClosed}
                    {' · '}
                    {formatNumber(year.documentCount)}
                  </option>
                ))}
              </select>
              <ChevronDownIcon className="pointer-events-none absolute end-2.5 size-4 text-paper-400" />
            </label>
          )}

          <RouterLink to="/help" aria-label={t.userGuide} title={t.userGuide} className={iconLink}>
            <HelpIcon />
          </RouterLink>

          <NotificationBell />

          <span className="mx-1 hidden h-6 w-px bg-paper-200 sm:block" aria-hidden />

          <button
            type="button"
            onClick={(e) => setAccountMenu(e.currentTarget)}
            aria-haspopup="menu"
            aria-expanded={!!accountMenu}
            className="flex max-w-[13rem] items-center gap-2 rounded-xl p-1 text-sm transition-colors hover:bg-paper-200/70 sm:pe-2"
          >
            <span className="grid size-8 shrink-0 place-items-center rounded-full fillo-gradient text-xs font-bold text-white ring-2 ring-paper-50">
              {(user?.displayName ?? '?').slice(0, 1)}
            </span>
            <span className="hidden min-w-0 text-start leading-tight sm:block">
              <span className="block truncate text-[13px] font-semibold text-ink-900">{user?.displayName}</span>
              <span className="block truncate text-[11px] text-paper-500" dir="ltr">
                {user?.username}
              </span>
            </span>
            <ChevronDownIcon className="hidden size-4 shrink-0 text-paper-400 sm:block" />
          </button>
          <Menu anchor={accountMenu} onClose={() => setAccountMenu(null)} className="w-60">
            <div className="-mx-1 -mt-1 mb-1 flex items-center gap-3 border-b border-paper-200 px-3 py-3">
              <span className="grid size-10 shrink-0 place-items-center rounded-full fillo-gradient text-sm font-bold text-white">
                {(user?.displayName ?? '?').slice(0, 1)}
              </span>
              <div className="min-w-0">
                <p className="truncate text-sm font-semibold text-ink-900">{user?.displayName}</p>
                <p className="truncate text-xs text-paper-500" dir="ltr">
                  {user?.username}
                </p>
              </div>
            </div>
            <RouterLink to="/settings" onClick={() => setAccountMenu(null)} className={menuItemClasses()}>
              <SettingsIcon />
              تنظیمات
            </RouterLink>
            <RouterLink to="/help" onClick={() => setAccountMenu(null)} className={menuItemClasses()}>
              <HelpIcon />
              {t.userGuide}
            </RouterLink>
            <RouterLink to="/account/password" onClick={() => setAccountMenu(null)} className={menuItemClasses()}>
              <KeyIcon />
              {directory.changePassword}
            </RouterLink>
            <div className="-mx-1 my-1 border-t border-paper-200" />
            <MenuItem
              danger
              onClick={() => {
                setAccountMenu(null);
                void signOut();
              }}
            >
              <LogoutIcon />
              {t.signOut}
            </MenuItem>
          </Menu>
        </div>
      </div>
    </header>
  );

  return (
    <div className="flex h-screen overflow-hidden">
      <aside
        className="hidden h-full min-h-0 shrink-0 flex-col border-e border-paper-200 bg-paper-50 lg:flex"
        style={{ width: sidebarWidth }}
      >
        <SidebarContent {...sidebarProps} showSearch={false} themeInputId="theme-light" />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {topBar}
        {fiscal.closed && (
          <div className="flex shrink-0 items-center gap-2 border-b border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-900 sm:px-6 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
            <CalendarIcon className="size-4 shrink-0" />
            <span>
              {t.fiscalYearClosedBanner} {t.fiscalYearFiling}
            </span>
          </div>
        )}
        <main className="min-h-0 flex-1 overflow-auto">
          <div
            key={location.pathname}
            className="page-enter mx-auto w-full max-w-[1440px] px-4 pt-5 pb-28 sm:px-6 sm:pt-6 lg:px-8 lg:pb-10"
          >
            {children}
          </div>
        </main>
      </div>

      <BottomNav
        pathname={location.pathname}
        autoManageActive={autoManageActive}
        homeHref={homeHref}
        newDocumentHref={newDocumentHref}
        pending={pending}
        canFile={canFile}
      />

      {drawerOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-ink-950/50 backdrop-blur-sm animate-[fade-in_0.2s_ease-out]"
            onClick={() => setDrawerOpen(false)}
            aria-hidden
          />
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t.menu}
            className="absolute inset-y-0 start-0 flex w-[86vw] flex-col overflow-hidden border-e border-paper-200 bg-paper-50 shadow-[var(--shadow-elevated)] animate-[slide-in_0.28s_cubic-bezier(0.2,0.8,0.2,1)]"
            style={{ maxWidth: sidebarWidth + 24 }}
          >
            <button
              type="button"
              onClick={() => setDrawerOpen(false)}
              aria-label="بستن"
              className={cx(iconLink, 'absolute top-3.5 end-3 z-10')}
            >
              <CloseIcon />
            </button>
            <SidebarContent
              {...sidebarProps}
              showSearch
              onNavigate={() => setDrawerOpen(false)}
              themeInputId="theme-light-drawer"
            />
          </div>
        </div>
      )}
    </div>
  );
}
