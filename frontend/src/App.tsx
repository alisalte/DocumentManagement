import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect } from 'react';
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router';
import { Layout } from './components/Layout';
import { Spinner } from './components/ui';
import { ApiError } from './lib/api';
import { BrowsePage } from './pages/BrowsePage';
import { DocumentPage } from './pages/DocumentPage';
import { LoginPage } from './pages/LoginPage';
import { NewDocumentPage } from './pages/NewDocumentPage';
import { PublicLinkPage } from './pages/PublicLinkPage';
import { RecycleBinPage } from './pages/RecycleBinPage';
import { SearchPage } from './pages/SearchPage';
import { SharedVersionPage } from './pages/SharedVersionPage';
import { SharedWithMePage } from './pages/SharedWithMePage';
import { AuditPage } from './pages/admin/AuditPage';
import { CategoriesPage } from './pages/admin/CategoriesPage';
import { DispositionPage } from './pages/admin/DispositionPage';
import { ImportPage } from './pages/admin/ImportPage';
import { GroupsPage } from './pages/admin/GroupsPage';
import { RolesPage } from './pages/admin/RolesPage';
import { UsersPage } from './pages/admin/UsersPage';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { DocumentTypeEditorPage } from './pages/admin/DocumentTypeEditorPage';
import { DocumentTypesPage } from './pages/admin/DocumentTypesPage';
import { SearchAdminPage } from './pages/admin/SearchAdminPage';
import { WorkflowEditorPage } from './pages/admin/WorkflowEditorPage';
import { WorkflowsPage } from './pages/admin/WorkflowsPage';
import { HelpGuidePage } from './pages/HelpGuidePage';
import { SettingsHome, SettingsLayout } from './pages/SettingsPage';
import { adminSections } from './pages/admin/sections';
import { TasksPage } from './pages/TasksPage';
import { FiscalYearProvider } from './lib/fiscalYear';
import { SessionProvider, useSession } from './session';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // A 403 or 404 is an answer, not a glitch; retrying it only delays the message.
      retry: (failureCount, error) =>
        !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
    },
  },
});

/**
 * The app shell: sign-in, the document browser, search, filing a new document, details with the
 * viewer and version history, the task inbox, shares, the recycle bin and the administration
 * screens, plus the public page behind an external link.
 * Persian, right to left, phone first.
 */
export default function App() {
  useEffect(() => {
    document.documentElement.dir = 'rtl';
    document.documentElement.lang = 'fa';
  }, []);

  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <FiscalYearProvider>
          <BrowserRouter>
            <Shell />
          </BrowserRouter>
        </FiscalYearProvider>
      </SessionProvider>
    </QueryClientProvider>
  );
}

/** Bookmarks and in-app links that still say /admin land on the same screen inside Settings. */
function LegacyAdminRedirect() {
  const { pathname, search } = useLocation();
  const rest = pathname.replace(/^\/admin/, '') || '/';
  return <Navigate to={`/settings${rest}${search}`} replace />;
}

function Shell() {
  const { user, ready } = useSession();
  const location = useLocation();

  // External links are for people without an account: no sign-in, no app frame.
  if (location.pathname.startsWith('/s/')) {
    return (
      <Routes>
        <Route path="/s/:token" element={<PublicLinkPage />} />
      </Routes>
    );
  }

  if (!ready) {
    return (
      <div className="grid min-h-screen place-items-center">
        <Spinner size="lg" />
      </div>
    );
  }

  if (!user) {
    return <LoginPage />;
  }

  // The server refuses everything else until the password is changed.
  if (user.mustChangePassword) {
    return <ChangePasswordPage forced />;
  }

  // Only hides screens that would be refused anyway; the server checks every call.
  const allowed = new Set(adminSections(user).filter((section) => section.allowed).map((section) => section.id));

  return (
    <Layout>
      <Routes>
        <Route path="/" element={<BrowsePage />} />
        <Route path="/new" element={<NewDocumentPage />} />
        <Route path="/documents/:id" element={<DocumentPage />} />
        <Route path="/recycle-bin" element={<RecycleBinPage />} />
        <Route path="/tasks" element={<TasksPage />} />
        <Route path="/shared" element={<SharedWithMePage />} />
        <Route path="/shared/:documentId/:versionId" element={<SharedVersionPage />} />
        <Route path="/search" element={<SearchPage />} />
        <Route path="/help" element={<HelpGuidePage />} />
        <Route path="/account/password" element={<ChangePasswordPage />} />
        <Route path="/settings" element={<SettingsLayout />}>
          <Route index element={<SettingsHome />} />
          {allowed.has('users') && <Route path="users" element={<UsersPage />} />}
          {allowed.has('groups') && <Route path="groups" element={<GroupsPage />} />}
          {allowed.has('roles') && <Route path="roles" element={<RolesPage />} />}
          {allowed.has('categories') && <Route path="categories" element={<CategoriesPage />} />}
          {allowed.has('document-types') && <Route path="document-types" element={<DocumentTypesPage />} />}
          {allowed.has('document-types') && <Route path="document-types/:id" element={<DocumentTypeEditorPage />} />}
          {allowed.has('workflows') && <Route path="workflows" element={<WorkflowsPage />} />}
          {allowed.has('workflows') && <Route path="workflows/:id" element={<WorkflowEditorPage />} />}
          {allowed.has('search') && <Route path="search" element={<SearchAdminPage />} />}
          {allowed.has('audit') && <Route path="audit" element={<AuditPage />} />}
          {allowed.has('disposition') && <Route path="disposition" element={<DispositionPage />} />}
          {allowed.has('imports') && <Route path="imports" element={<ImportPage />} />}
        </Route>
        <Route path="/admin/*" element={<LegacyAdminRedirect />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Layout>
  );
}
