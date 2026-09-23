import { CacheProvider } from '@emotion/react';
import { Box, CircularProgress, CssBaseline, ThemeProvider } from '@mui/material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { Layout } from './components/Layout';
import { ApiError } from './lib/api';
import { BrowsePage } from './pages/BrowsePage';
import { DocumentPage } from './pages/DocumentPage';
import { LoginPage } from './pages/LoginPage';
import { NewDocumentPage } from './pages/NewDocumentPage';
import { RecycleBinPage } from './pages/RecycleBinPage';
import { DocumentTypeEditorPage } from './pages/admin/DocumentTypeEditorPage';
import { DocumentTypesPage } from './pages/admin/DocumentTypesPage';
import { SessionProvider, useSession } from './session';
import { createAppTheme, rtlCache } from './theme';

const theme = createAppTheme('rtl');

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
 * Phase 2 shell: sign-in, the document browser, filing a new document, details with version
 * history, and the recycle bin. Persian, right to left, phone first.
 */
export default function App() {
  useEffect(() => {
    document.documentElement.dir = 'rtl';
    document.documentElement.lang = 'fa';
  }, []);

  return (
    <CacheProvider value={rtlCache}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <QueryClientProvider client={queryClient}>
          <SessionProvider>
            <BrowserRouter>
              <Shell />
            </BrowserRouter>
          </SessionProvider>
        </QueryClientProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}

function Shell() {
  const { user, ready } = useSession();

  if (!ready) {
    return (
      <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <CircularProgress />
      </Box>
    );
  }

  if (!user) {
    return <LoginPage />;
  }

  // Only hides screens that would be refused anyway; the server checks every call.
  const canManageTypes = user.isSystemAdmin || user.systemPermissions.includes('ADMIN_MANAGE_DOCUMENT_TYPES');

  return (
    <Layout>
      <Routes>
        <Route path="/" element={<BrowsePage />} />
        <Route path="/new" element={<NewDocumentPage />} />
        <Route path="/documents/:id" element={<DocumentPage />} />
        <Route path="/recycle-bin" element={<RecycleBinPage />} />
        {canManageTypes && <Route path="/admin/document-types" element={<DocumentTypesPage />} />}
        {canManageTypes && <Route path="/admin/document-types/:id" element={<DocumentTypeEditorPage />} />}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Layout>
  );
}
