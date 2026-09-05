import { useQuery } from '@tanstack/react-query';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Chip,
  CircularProgress,
  Divider,
  Stack,
  Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import { api } from '../api/client';

const money = (n?: number | null) =>
  n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

export function TradesPage() {
  const { data, isLoading } = useQuery({ queryKey: ['trades'], queryFn: api.trades });

  if (isLoading || !data) return <CircularProgress />;

  return (
    <Stack spacing={2}>
      <Typography variant="h4">Trades</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
        History of cards you've traded away, newest first.
      </Typography>

      {data.length === 0 ? (
        <Typography color="text.secondary">No trades recorded yet.</Typography>
      ) : (
        data.map((t) => (
          <Accordion key={t.id} variant="outlined" disableGutters>
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Stack
                direction="row"
                spacing={1}
                alignItems="center"
                sx={{ width: '100%', flexWrap: 'wrap' }}
                useFlexGap
              >
                <Typography sx={{ flexGrow: 1, fontWeight: 600 }}>{t.label}</Typography>
                {t.hasPhoto && <PhotoCameraIcon fontSize="small" color="disabled" />}
                {t.valueDelta != null && (
                  <Chip
                    size="small"
                    color={t.valueDelta >= 0 ? 'success' : 'warning'}
                    label={`${t.valueDelta >= 0 ? '+' : ''}${money(t.valueDelta)}`}
                  />
                )}
                {t.replacementCount > 0 && (
                  <Chip size="small" variant="outlined" label={`${t.replacementCount} replaced`} />
                )}
                <Typography variant="caption" color="text.secondary">
                  {new Date(t.createdAt).toLocaleDateString()}
                </Typography>
              </Stack>
            </AccordionSummary>
            <AccordionDetails>
              <Stack spacing={1}>
                <Stack direction="row" spacing={2}>
                  <Typography variant="body2">
                    Out: <strong>{money(t.outgoingValue)}</strong>
                  </Typography>
                  <Typography variant="body2">
                    Received: <strong>{money(t.receivedValue)}</strong>
                  </Typography>
                </Stack>
                {t.note && (
                  <Typography variant="body2" color="text.secondary">
                    {t.note}
                  </Typography>
                )}
                <Divider />
                <Typography variant="subtitle2">Traded away</Typography>
                {t.outgoingCards.map((c, i) => (
                  <Typography key={i} variant="body2" color="text.secondary">
                    {c.cardName}
                    {c.setCode ? ` · ${c.setCode.toUpperCase()}` : ''}
                    {c.collectorNumber ? ` #${c.collectorNumber}` : ''}
                    {c.foil ? ' · Foil' : ''}
                    {c.isOffDatabase ? ' · (off-catalog)' : ''}
                    {c.estimatedValue != null ? ` — ${money(c.estimatedValue)}` : ''}
                  </Typography>
                ))}
              </Stack>
            </AccordionDetails>
          </Accordion>
        ))
      )}
    </Stack>
  );
}
