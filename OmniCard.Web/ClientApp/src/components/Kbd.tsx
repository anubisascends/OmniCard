import { Box } from '@mui/material';

/** A keyboard key cap, for shortcut legends. */
export function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <Box
      component="kbd"
      sx={{
        px: 0.75,
        py: 0.25,
        border: 1,
        borderColor: 'divider',
        borderBottomWidth: 2,
        borderRadius: 1,
        fontFamily: 'inherit',
        fontSize: '0.8rem',
        bgcolor: 'action.hover',
        whiteSpace: 'nowrap',
      }}
    >
      {children}
    </Box>
  );
}
