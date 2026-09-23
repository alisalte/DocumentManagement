import {
  AppBar,
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
import { useState, type ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { api } from '../lib/api';
import { useSession } from '../session';
import { t } from '../strings';
import { CategoryTree } from './CategoryTree';

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
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Button color="inherit" component={RouterLink} to="/new">
              {t.newDocument}
            </Button>
            {isDesktop && (
              <Button color="inherit" component={RouterLink} to="/recycle-bin">
                {t.recycleBin}
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
          <Button component={RouterLink} to="/recycle-bin" onClick={() => setOpen(false)} sx={{ m: 2 }}>
            {t.recycleBin}
          </Button>
        </Drawer>
      )}

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 1.5, sm: 3 } }}>
        <Toolbar />
        {children}
      </Box>
    </Box>
  );
}
