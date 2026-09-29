import { createTheme, type Theme } from '@mui/material/styles'
import type { ThemeMode } from '@/app/store/uiStore'

const fontFamily = [
  '"Inter Variable"',
  'Inter',
  '"Segoe UI"',
  'system-ui',
  '-apple-system',
  'Roboto',
  'Helvetica',
  'Arial',
  'sans-serif',
].join(',')

/**
 * Enterprise-dashboard theme: neutral surfaces, one strong accent, dense-but-readable type, and visible focus rings
 * (keyboard users are first-class). Colours meet WCAG AA contrast against their backgrounds.
 */
export function createAppTheme(mode: ThemeMode): Theme {
  const dark = mode === 'dark'

  return createTheme({
    palette: {
      mode,
      primary: { main: dark ? '#6f96ff' : '#1f5eff', contrastText: dark ? '#0b1020' : '#ffffff' },
      secondary: { main: dark ? '#9aa7bd' : '#4a5b70' },
      success: { main: dark ? '#4cc38a' : '#1a7f4b' },
      warning: { main: dark ? '#f0b44c' : '#9a5b00' },
      error: { main: dark ? '#ff7a7a' : '#c62828' },
      info: { main: dark ? '#5cb8ff' : '#0b6ea8' },
      background: dark
        ? { default: '#0d1220', paper: '#151b2c' }
        : { default: '#f4f6fa', paper: '#ffffff' },
      divider: dark ? 'rgba(255,255,255,0.10)' : 'rgba(16,24,40,0.10)',
      text: dark
        ? { primary: '#e8ecf5', secondary: '#a4afc4' }
        : { primary: '#152033', secondary: '#4f5d73' },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily,
      fontSize: 14,
      h1: { fontSize: '2rem', fontWeight: 700, letterSpacing: '-0.02em' },
      h2: { fontSize: '1.5rem', fontWeight: 700, letterSpacing: '-0.015em' },
      h3: { fontSize: '1.25rem', fontWeight: 650, letterSpacing: '-0.01em' },
      h4: { fontSize: '1.125rem', fontWeight: 650 },
      h5: { fontSize: '1rem', fontWeight: 650 },
      h6: { fontSize: '0.9375rem', fontWeight: 650 },
      subtitle1: { fontWeight: 600 },
      subtitle2: { fontWeight: 600, fontSize: '0.8125rem' },
      button: { textTransform: 'none', fontWeight: 600 },
      overline: { fontWeight: 700, letterSpacing: '0.08em' },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          body: { fontFeatureSettings: '"cv11", "ss01"' },
          '*:focus-visible': { outline: '2px solid currentColor', outlineOffset: 2 },
        },
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: { root: { borderRadius: 8 } },
      },
      MuiTextField: { defaultProps: { size: 'small', fullWidth: true } },
      MuiSelect: { defaultProps: { size: 'small' } },
      MuiAutocomplete: { defaultProps: { size: 'small' } },
      MuiCard: {
        defaultProps: { variant: 'outlined' },
        styleOverrides: { root: { borderRadius: 12 } },
      },
      MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
      MuiTableCell: {
        styleOverrides: {
          head: { fontWeight: 650, fontSize: '0.75rem', letterSpacing: '0.04em', textTransform: 'uppercase' },
        },
      },
      MuiChip: { styleOverrides: { root: { fontWeight: 600 } } },
      MuiTooltip: { defaultProps: { arrow: true } },
      MuiDrawer: { styleOverrides: { paper: { backgroundImage: 'none' } } },
    },
  })
}
