/** Same palette as the web app: indigo brand on cool slate surfaces. */
export const colors = {
  background: '#f5f6fa',
  card: '#ffffff',
  /** Headings and primary text. */
  ink: '#171a33',
  /** Body text. */
  text: '#363c4e',
  muted: '#6b7287',
  subtle: '#a2a8b8',
  line: '#e8eaf1',
  lineStrong: '#d7dae4',
  accent: '#5048e5',
  accentPressed: '#4338ca',
  accentSoft: '#f0f2ff',
  accentLine: '#c9d0ff',
  danger: '#e11d48',
  dangerSoft: '#fff1f2',
  success: '#059669',
  successSoft: '#ecfdf5',
  warning: '#d97706',
  warningSoft: '#fffbeb',
};

export const space = {
  xs: 6,
  sm: 10,
  md: 16,
  lg: 24,
  xl: 36,
};

export const radius = {
  sm: 8,
  md: 12,
  lg: 16,
  pill: 999,
};

export const shadow = {
  card: {
    shadowColor: '#101828',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 3,
    elevation: 1,
  },
  raised: {
    shadowColor: '#101828',
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.12,
    shadowRadius: 16,
    elevation: 6,
  },
  brand: {
    shadowColor: '#5048e5',
    shadowOffset: { width: 0, height: 6 },
    shadowOpacity: 0.35,
    shadowRadius: 12,
    elevation: 6,
  },
};
