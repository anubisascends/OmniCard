import { useState } from 'react';
import { useParams, Link as RouterLink } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import {
  Box,
  Breadcrumbs,
  Button,
  CircularProgress,
  Link,
  Paper,
  Stack,
  Tab,
  Tabs,
  Tooltip,
  Typography,
} from '@mui/material';
import { api } from '../api/client';
import type { BinderSlotDto } from '../api/types';

// Card-back image slugs indexed by CardGame enum order (Mtg=0 … FinalFantasy=5). Assets live at
// wwwroot/img/card-back-{slug}.png (served from root, so absolute paths resolve under /app too).
const CARD_BACK_SLUGS = ['mtg', 'optcg', 'riftbound', 'pokemon', 'yugioh', 'fftcg'];
const cardBackSrc = (game: number): string | undefined => {
  const slug = CARD_BACK_SLUGS[game];
  return slug ? `/img/card-back-${slug}.png` : undefined;
};

/** An empty pocket whose mirrored pocket on the reverse side of the sheet holds a card: show that
 * game's card back (as if seeing the back of the card behind the page). Degrades to a subtle
 * gradient when the user hasn't supplied the PNG. */
function ReverseCardBack({ game }: { game: number }) {
  const src = cardBackSrc(game);
  return (
    <Tooltip title="Card on the reverse side of this sheet">
      <Box
        sx={{
          width: '100%',
          height: '100%',
          display: 'grid',
          placeItems: 'center',
          background: (t) =>
            `repeating-linear-gradient(135deg, ${t.palette.action.hover}, ${t.palette.action.hover} 6px, ${t.palette.action.selected} 6px, ${t.palette.action.selected} 12px)`,
        }}
      >
        {src && (
          <img
            src={src}
            alt=""
            style={{ width: '100%', height: '100%', objectFit: 'contain', opacity: 0.85 }}
            onError={(e) => e.currentTarget.remove()}
          />
        )}
      </Box>
    </Tooltip>
  );
}

function SlotGrid({
  slots,
  columns,
  pageLabel,
}: {
  slots: BinderSlotDto[];
  columns: number;
  pageLabel: string;
}) {
  return (
    <Paper variant="outlined" sx={{ p: 1, flex: 1 }}>
      <Typography variant="caption" color="text.secondary">
        {pageLabel}
      </Typography>
      <Box
        sx={{
          display: 'grid',
          gap: 0.5,
          gridTemplateColumns: `repeat(${columns}, 1fr)`,
          mt: 0.5,
        }}
      >
        {slots.map((s) => (
          <Box
            key={s.slotIndex}
            sx={{
              aspectRatio: '0.72',
              border: '1px dashed',
              borderColor: 'divider',
              borderRadius: 1,
              overflow: 'hidden',
              bgcolor: 'action.hover',
              display: 'grid',
              placeItems: 'center',
            }}
          >
            {s.card?.imageUrl ? (
              <Tooltip title={`${s.card.name} · ${s.card.condition}${s.card.foil ? ' · Foil' : ''}`}>
                <img
                  src={s.card.imageUrl}
                  alt={s.card.name}
                  style={{ width: '100%', height: '100%', objectFit: 'contain' }}
                />
              </Tooltip>
            ) : s.reverseGame != null ? (
              <ReverseCardBack game={s.reverseGame} />
            ) : null}
          </Box>
        ))}
      </Box>
    </Paper>
  );
}

export function BinderPage() {
  const { id } = useParams();
  const binderId = Number(id);
  const [spread, setSpread] = useState(0);

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['binder', binderId, spread],
    queryFn: () => api.binder(binderId, spread),
    placeholderData: keepPreviousData,
  });

  if (isLoading || !data) return <CircularProgress />;

  return (
    <Stack spacing={2}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/locations">
          Locations
        </Link>
        <Link component={RouterLink} to={`/location/${binderId}`}>
          {data.containerName}
        </Link>
        <Typography color="text.primary">Binder</Typography>
      </Breadcrumbs>

      <Stack direction="row" spacing={2} alignItems="center">
        <Typography variant="h4">{data.containerName}</Typography>
        <Typography variant="body2" color="text.secondary">
          {data.pageRangeLabel} · {data.totalPages} pages
        </Typography>
        {isFetching && <CircularProgress size={16} />}
      </Stack>

      <Stack direction="row" spacing={1} alignItems="center">
        <Button
          size="small"
          disabled={spread <= 0}
          onClick={() => setSpread((s) => Math.max(0, s - 1))}
        >
          ‹ Prev
        </Button>
        <Tabs
          value={data.spreadIndex}
          onChange={(_, v) => setSpread(v)}
          variant="scrollable"
          scrollButtons="auto"
          sx={{ flexGrow: 1, minHeight: 36 }}
        >
          {data.spreadTabs.map((t) => (
            <Tab key={t.index} value={t.index} label={t.label} sx={{ minHeight: 36 }} />
          ))}
        </Tabs>
        <Button
          size="small"
          disabled={spread >= data.totalSpreads - 1}
          onClick={() => setSpread((s) => Math.min(data.totalSpreads - 1, s + 1))}
        >
          Next ›
        </Button>
      </Stack>

      <Stack direction="row" spacing={2}>
        {data.leftPageNumber != null ? (
          <SlotGrid
            slots={data.leftSlots}
            columns={data.columns}
            pageLabel={`Page ${data.leftPageNumber}`}
          />
        ) : (
          <Box sx={{ flex: 1 }} />
        )}
        {data.rightPageNumber != null && (
          <SlotGrid
            slots={data.rightSlots}
            columns={data.columns}
            pageLabel={`Page ${data.rightPageNumber}`}
          />
        )}
      </Stack>
    </Stack>
  );
}
