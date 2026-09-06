import { createTheme } from '@mui/material/styles';

// Material Design theme approximating the desktop app's MaterialDesignThemes look.
// Follows the OS light/dark preference.
export function buildTheme(mode: 'light' | 'dark') {
  return createTheme({
    palette: {
      mode,
      primary: { main: '#5b6bc0' },
      secondary: { main: '#26a69a' },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontSize: 13,
    },
  });
}
