import {
  AppBar,
  Badge,
  InputBase,
  Box,
  Button,
  Drawer,
  Stack,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent, type ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { api } from '../lib/api';
import { useSession } from '../session';
import { t } from '../strings';
import { CategoryTree } from './CategoryTree';
import { s as sharing } from './sharing/sharingStrings';
import { w } from './workflow/workflowStrings';

const drawerWidth = 280;

/**
 * App frame. On a desktop the category tree is a permanent side panel; on a phone it is a
 * drawer behind a button, so the document list gets the full width.
 */
export function Layout({ children }: { children: ReactNode }) {
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'));
  const [open, setOpen] = useState(false);
  const { user, signOut } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const selectedCategory = params.get('category');
  const canManageTypes = !!user && (user.isSystemAdmin || user.systemPermissions.includes('ADMIN_MANAGE_DOCUMENT_TYPES'));
  const canManageWorkflows = !!user && (user.isSystemAdmin || user.systemPermissions.includes('ADMIN_MANAGE_WORKFLOWS'));
  const canManageSearch = !!user && (user.isSystemAdmin || user.systemPermissions.includes('ADMIN_MANAGE_SEARCH'));
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

  const tree = (
    <Box sx={{ overflowY: 'auto' }}>
      <Typography variant="overline" sx={{ px: 2, pt: 1, display: 'block' }} color="text.secondary">
        {t.categories}
      </Typography>
      <CategoryTree
        categories={categories.data ?? []}
        selectedId={selectedCategory}
        onSelect={selectCategory}
      />
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" sx={{ zIndex: (current) => current.zIndex.drawer + 1 }}>
        <Toolbar sx={{ gap: 1 }}>
          {!isDesktop && (
            <Button color="inherit" onClick={() => setOpen(true)} aria-label={t.menu}>
              ☰
            </Button>
          )}
          <Typography
            variant="h6"
            component={RouterLink}
            to="/"
            sx={{ flexGrow: 1, color: 'inherit', textDecoration: 'none' }}
            noWrap
          >
            {t.appTitle}
          </Typography>
          {isDesktop ? (
            <Box
              component="form"
              onSubmit={submitSearch}
              role="search"
              sx={{ bgcolor: 'rgba(255,255,255,0.15)', borderRadius: 1, px: 1.5, width: 280 }}
            >
              <InputBase
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={t.searchEverything}
                inputProps={{ 'aria-label': t.searchEverything, enterKeyHint: 'search' }}
                sx={{ color: 'inherit', width: '100%' }}
              />
            </Box>
          ) : (
            <Button color="inherit" component={RouterLink} to="/search" aria-label={t.searchEverything}>
              {t.searchButton}
            </Button>
          )}
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Button color="inherit" component={RouterLink} to="/tasks">
              <Badge color="secondary" badgeContent={pending} max={99}>
                {w.inbox}
              </Badge>
            </Button>
            {isDesktop && (
              <Button color="inherit" component={RouterLink} to="/shared">
                {sharing.sharedWithMe}
              </Button>
            )}
            <Button color="inherit" component={RouterLink} to="/new">
              {t.newDocument}
            </Button>
            {isDesktop && (
              <Button color="inherit" component={RouterLink} to="/recycle-bin">
                {t.recycleBin}
              </Button>
            )}
            {isDesktop && canManageTypes && (
              <Button color="inherit" component={RouterLink} to="/admin/document-types">
                {t.documentTypes}
              </Button>
            )}
            {isDesktop && canManageWorkflows && (
              <Button color="inherit" component={RouterLink} to="/admin/workflows">
                {w.workflows}
              </Button>
            )}
            {isDesktop && canManageSearch && (
              <Button color="inherit" component={RouterLink} to="/admin/search">
                {t.searchAdmin}
              </Button>
            )}
            {isDesktop && (
              <Typography variant="body2" sx={{ opacity: 0.85 }}>
                {user?.displayName}
              </Typography>
            )}
            <Button color="inherit" onClick={signOut}>
              {t.signOut}
            </Button>
          </Stack>
        </Toolbar>
      </AppBar>

      {isDesktop ? (
        <Drawer
          variant="permanent"
          sx={{
            width: drawerWidth,
            flexShrink: 0,
            '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' },
          }}
        >
          <Toolbar />
          {tree}
        </Drawer>
      ) : (
        <Drawer
          open={open}
          onClose={() => setOpen(false)}
          sx={{ '& .MuiDrawer-paper': { width: '85vw', maxWidth: drawerWidth } }}
        >
          <Toolbar />
          {tree}
          <Button component={RouterLink} to="/shared" onClick={() => setOpen(false)} sx={{ m: 2, mb: 0 }}>
            {sharing.sharedWithMe}
          </Button>
          <Button component={RouterLink} to="/recycle-bin" onClick={() => setOpen(false)} sx={{ m: 2, mb: 0 }}>
            {t.recycleBin}
          </Button>
          {canManageTypes && (
            <Button component={RouterLink} to="/admin/document-types" onClick={() => setOpen(false)} sx={{ m: 2, mb: 0 }}>
              {t.documentTypes}
            </Button>
          )}
          {canManageWorkflows && (
            <Button component={RouterLink} to="/admin/workflows" onClick={() => setOpen(false)} sx={{ m: 2, mb: 0 }}>
              {w.workflows}
            </Button>
          )}
          {canManageSearch && (
            <Button component={RouterLink} to="/admin/search" onClick={() => setOpen(false)} sx={{ m: 2 }}>
              {t.searchAdmin}
            </Button>
          )}
        </Drawer>
      )}

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 1.5, sm: 3 } }}>
        <Toolbar />
        {children}
      </Box>
    </Box>
  );
}
