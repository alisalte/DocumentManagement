import createCache from '@emotion/cache';
import { createTheme } from '@mui/material/styles';
import { prefixer } from 'stylis';
import rtlPlugin from 'stylis-plugin-rtl';

/**
 * Persian-first, right-to-left UI (decision D4). Localisation is presentation only: the API and
 * the database always speak UTC and ISO-8601, and nothing here leaks into persistence.
 */
export const rtlCache = createCache({
  key: 'muirtl',
  stylisPlugins: [prefixer, rtlPlugin],
});

export const ltrCache = createCache({ key: 'mui' });

export const createAppTheme = (direction: 'rtl' | 'ltr') =>
  createTheme({
    direction,
    typography: {
      // Vazirmatn renders Persian well; the rest are fallbacks for machines without it.
      fontFamily: ['Vazirmatn', 'Segoe UI', 'Roboto', 'Helvetica', 'Arial', 'sans-serif'].join(','),
    },
    palette: {
      primary: { main: '#1d6f6f' },
      secondary: { main: '#8a5a2b' },
    },
    breakpoints: {
      // Mobile first from the start: the archive is used on phones in the field.
      values: { xs: 0, sm: 600, md: 900, lg: 1200, xl: 1536 },
    },
  });
