import { createTheme } from '@mui/material/styles';

// Material Design theme approximating the desktop app's MaterialDesignThemes look.
// Follows the OS light/dark preference. The kiosk passes a larger `fontSize` for its touch screen.
//
// Phone widths (below `sm`) get a few global adjustments so every page works without per-page code:
// page titles (h4) shrink, tab strips scroll, wide dialogs (md and up) go full screen and narrow ones
// keep only a small margin.
export function buildTheme(mode: 'light' | 'dark', fontSize = 13) {
  const base = createTheme({
    palette: {
      mode,
      primary: { main: '#5b6bc0' },
      secondary: { main: '#26a69a' },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontSize,
    },
  });
  const phone = base.breakpoints.down('sm');
  return createTheme(base, {
    typography: {
      h4: { [phone]: { fontSize: '1.6rem' } },
      h5: { [phone]: { fontSize: '1.3rem' } },
    },
    components: {
      MuiTabs: {
        defaultProps: { variant: 'scrollable', scrollButtons: 'auto', allowScrollButtonsMobile: true },
      },
      MuiDialog: {
        styleOverrides: {
          // One rule set so its order is explicit: the full-screen sizes come last and win.
          paper: {
            [phone]: {
              margin: 12,
              '&.MuiDialog-paperFullWidth': { width: 'calc(100% - 24px)' },
              '&.MuiDialog-paperScrollPaper': { maxHeight: 'calc(100% - 24px)' },
              '&.MuiDialog-paperWidthMd, &.MuiDialog-paperWidthLg, &.MuiDialog-paperWidthXl, &.MuiDialog-paperFullScreen': {
                margin: 0,
                width: '100%',
                maxWidth: '100%',
                height: '100%',
                maxHeight: '100%',
                borderRadius: 0,
              },
            },
          },
        },
      },
    },
  });
}
