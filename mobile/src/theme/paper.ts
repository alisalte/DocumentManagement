import { MD3LightTheme, configureFonts, type MD3Theme } from 'react-native-paper';
import { colors } from '../theme';

/** Paper components follow the app palette in src/theme.ts. */
export const paperColors = {
  primary: colors.accent,
  secondary: colors.text,
  error: colors.danger,
  surface: colors.card,
};

export const paperTheme: MD3Theme = {
  ...MD3LightTheme,
  // Paper multiplies this by 4 for buttons and outlined inputs: 12px corners.
  roundness: 3,
  colors: {
    ...MD3LightTheme.colors,
    primary: paperColors.primary,
    primaryContainer: colors.accentSoft,
    secondary: paperColors.secondary,
    error: paperColors.error,
    background: colors.background,
    surface: paperColors.surface,
    surfaceVariant: colors.background,
    onSurface: colors.ink,
    onSurfaceVariant: colors.muted,
    outline: colors.lineStrong,
    outlineVariant: colors.line,
  },
  fonts: configureFonts({ config: { fontFamily: 'Vazir' } }),
};
