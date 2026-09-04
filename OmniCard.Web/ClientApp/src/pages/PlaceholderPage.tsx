import { Alert, Stack, Typography } from '@mui/material';

/** Temporary stub for screens not yet built in the migration (Sets, Import, Sales, …). */
export function PlaceholderPage({ title }: { title: string }) {
  return (
    <Stack spacing={2}>
      <Typography variant="h4">{title}</Typography>
      <Alert severity="info">This screen is coming in a later phase of the web migration.</Alert>
    </Stack>
  );
}
