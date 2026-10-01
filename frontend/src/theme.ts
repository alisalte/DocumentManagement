const storageKey = 'dms-theme';

export type Theme = 'light' | 'dark';

/** Saved choice, otherwise the light surfaces the app is designed around. */
export function readTheme(): Theme {
  try {
    const saved = localStorage.getItem(storageKey);
    if (saved === 'light' || saved === 'dark') return saved;
  } catch {
    // Private mode can refuse storage; the light theme still applies.
  }
  return 'light';
}

export function applyTheme(theme: Theme) {
  document.documentElement.classList.toggle('dark', theme === 'dark');
  document.documentElement.style.colorScheme = theme;
  try {
    localStorage.setItem(storageKey, theme);
  } catch {
    // The class still changes for this visit.
  }
}
