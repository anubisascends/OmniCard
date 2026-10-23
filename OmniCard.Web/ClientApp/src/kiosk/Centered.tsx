import type { ReactNode } from 'react';
import { Box, Paper, Stack } from '@mui/material';

/** A full-screen, centred card for the kiosk's single-purpose screens (enroll, welcome, closed, sent). */
export function Centered({ children }: { children: ReactNode }) {
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper sx={{ p: { xs: 3, sm: 5 }, maxWidth: 640, width: '100%' }}>
        <Stack spacing={3} alignItems="center" textAlign="center">
          {children}
        </Stack>
      </Paper>
    </Box>
  );
}
