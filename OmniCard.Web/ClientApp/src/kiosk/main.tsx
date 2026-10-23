import { StrictMode, useMemo } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CssBaseline, ThemeProvider, useMediaQuery } from '@mui/material';
import { buildTheme } from '../theme';
import { KioskApp } from './KioskApp';
import '../i18n';

// The customer kiosk (served at /kiosk): its own page with no staff shell, sign-in or router. It talks
// only to /api/kiosk, authenticated by the kiosk device cookie an administrator issues on enrollment.

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
});

function Root() {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)');
  // A touch screen read at arm's length: a larger base font than the staff app.
  const theme = useMemo(() => buildTheme(prefersDark ? 'dark' : 'light', 16), [prefersDark]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <KioskApp />
      </QueryClientProvider>
    </ThemeProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
