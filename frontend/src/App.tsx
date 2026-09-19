import { CacheProvider } from '@emotion/react';
import {
  Alert,
  AppBar,
  Box,
  Button,
  Chip,
  Container,
  CssBaseline,
  Paper,
  Stack,
  TextField,
  ThemeProvider,
  Toolbar,
  Typography,
} from '@mui/material';
import { useEffect, useState } from 'react';
import { api, ApiError, type CurrentUser } from './lib/api';
import { formatDateTime } from './lib/dates';
import { createAppTheme, rtlCache } from './theme';

const strings = {
  title: 'بایگانی اسناد',
  username: 'نام کاربری',
  password: 'گذرواژه',
  signIn: 'ورود',
  signOut: 'خروج',
  signedInAs: 'وارد شده به عنوان',
  mustChangePassword: 'باید گذرواژه خود را تغییر دهید.',
  administrator: 'مدیر سامانه',
  permissions: 'مجوزهای سامانه‌ای',
  now: 'اکنون',
  phaseNotice: 'فاز ۱: احراز هویت، مجوزها، ممیزی. مدیریت اسناد در فاز بعد اضافه می‌شود.',
};

/**
 * Phase 1 shell: enough UI to sign in and prove the Persian/RTL foundation works end to end.
 * The document browser, upload and admin screens arrive with their phases.
 */
export default function App() {
  const theme = createAppTheme('rtl');
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    document.documentElement.dir = 'rtl';
    document.documentElement.lang = 'fa';
  }, []);

  async function signIn(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.login(username, password);
      setUser(await api.me());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : String(caught));
    } finally {
      setBusy(false);
    }
  }

  async function signOut() {
    await api.logout();
    setUser(null);
    setPassword('');
  }

  return (
    <CacheProvider value={rtlCache}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <AppBar position="static" color="primary">
          <Toolbar>
            <Typography variant="h6" sx={{ flexGrow: 1 }}>
              {strings.title}
            </Typography>
            {user && (
              <Button color="inherit" onClick={signOut}>
                {strings.signOut}
              </Button>
            )}
          </Toolbar>
        </AppBar>

        <Container maxWidth="sm" sx={{ py: { xs: 2, sm: 4 } }}>
          <Alert severity="info" sx={{ mb: 2 }}>
            {strings.phaseNotice}
          </Alert>

          {!user ? (
            <Paper component="form" onSubmit={signIn} sx={{ p: { xs: 2, sm: 3 } }} elevation={2}>
              <Stack spacing={2}>
                <TextField
                  label={strings.username}
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                  autoComplete="username"
                  fullWidth
                  required
                />
                <TextField
                  label={strings.password}
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  type="password"
                  autoComplete="current-password"
                  fullWidth
                  required
                />
                {error && <Alert severity="error">{error}</Alert>}
                <Button type="submit" variant="contained" disabled={busy} size="large">
                  {strings.signIn}
                </Button>
              </Stack>
            </Paper>
          ) : (
            <Paper sx={{ p: { xs: 2, sm: 3 } }} elevation={2}>
              <Stack spacing={2}>
                <Typography variant="h6">
                  {strings.signedInAs}: {user.displayName}
                </Typography>
                {user.isSystemAdmin && <Chip color="secondary" label={strings.administrator} />}
                {user.mustChangePassword && (
                  <Alert severity="warning">{strings.mustChangePassword}</Alert>
                )}
                <Box>
                  <Typography variant="subtitle2">{strings.permissions}</Typography>
                  <Stack direction="row" flexWrap="wrap" gap={0.5} sx={{ mt: 1 }}>
                    {user.systemPermissions.map((permission) => (
                      <Chip key={permission} size="small" label={permission} />
                    ))}
                  </Stack>
                </Box>
                <Typography variant="body2" color="text.secondary">
                  {strings.now}: {formatDateTime(new Date().toISOString())}
                </Typography>
              </Stack>
            </Paper>
          )}
        </Container>
      </ThemeProvider>
    </CacheProvider>
  );
}
