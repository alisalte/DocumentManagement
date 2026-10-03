import { MD3LightTheme, configureFonts, type MD3Theme } from 'react-native-paper';

/** Colors from the Callstack Paper login template. */
export const paperColors = {
  primary: '#600EE6',
  secondary: '#414757',
  error: '#f13a59',
  surface: '#ffffff',
};

export const paperTheme: MD3Theme = {
  ...MD3LightTheme,
  roundness: 4,
  colors: {
    ...MD3LightTheme.colors,
    primary: paperColors.primary,
    secondary: paperColors.secondary,
    error: paperColors.error,
    background: paperColors.surface,
    surface: paperColors.surface,
    onSurface: paperColors.secondary,
    outline: '#D8DBE3',
  },
  fonts: configureFonts({ config: { fontFamily: 'Vazir' } }),
};
