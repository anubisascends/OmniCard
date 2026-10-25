import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Link,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableRow,
  Typography,
} from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { ApiError, api } from '../api/client';
import { CardImage } from '../components/CardImage';
import { CardEditDrawer } from '../components/dialogs/CardEditDrawer';
import { usePermissions } from '../context/usePermissions';
import { useFormatters } from '../i18n/format';
import { languageName } from '../lib/cardLanguages';

/**
 * One owned card (lot) on its own page — where the card-page QR code on a printed label leads. Shows
 * what the card is, where it's kept and what it's worth, with the usual editor for users who may edit.
 */
export function CardDetailPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { id } = useParams();
  const cardId = Number(id);
  const { can } = usePermissions();
  const [editing, setEditing] = useState(false);
  const cardQuery = useQuery({ queryKey: ['card', cardId], queryFn: () => api.card(cardId), retry: false });
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });

  if (cardQuery.isLoading) return <CircularProgress />;
  const card = cardQuery.data;
  if (!card) {
    const notFound = cardQuery.error instanceof ApiError && cardQuery.error.status === 404;
    return <Alert severity={notFound ? 'info' : 'error'}>{notFound ? t('labels.cardPage.notFound') : (cardQuery.error as Error)?.message}</Alert>;
  }

  const gameName = games.data?.find((g) => g.id === card.game)?.displayName ?? card.game;
  const rows: [string, React.ReactNode][] = [
    [t('labels.cardPage.game'), gameName],
    [t('labels.cardPage.set'), `${card.setName} (${card.setCode.toUpperCase()})`],
    [t('labels.cardPage.number'), card.number],
    [t('common.labels.rarity'), card.rarity],
    [t('labels.cardPage.condition'), card.condition],
    [t('labels.cardPage.language'), languageName(t, card.language)],
    [t('labels.cardPage.finish'), card.isFoil ? card.foilType || t('common.labels.foil') : t('labels.cardPage.nonFoil')],
    [t('labels.cardPage.quantity'), fmt.number(card.quantity)],
    [
      t('labels.cardPage.location'),
      card.containerId != null && card.containerName ? (
        <Link component={RouterLink} to={`/location/${card.containerId}`}>
          {card.containerName}
        </Link>
      ) : (
        t('labels.cardPage.noLocation')
      ),
    ],
    [t('labels.cardPage.marketPrice'), card.marketPrice > 0 ? fmt.money(card.marketPrice) : t('labels.cardPage.unpriced')],
  ];
  if (card.listingStatus) rows.push([t('labels.cardPage.listing'), t(`common.listingStatus.${card.listingStatus}`, card.listingStatus)]);

  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'center' }}>
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {card.name}
        </Typography>
        {card.tcgPlayerUrl && (
          <Button variant="outlined" endIcon={<OpenInNewIcon />} href={card.tcgPlayerUrl} target="_blank" rel="noopener">
            {t('labels.cardPage.openTcgplayer')}
          </Button>
        )}
        {can('collection.edit') && (
          <Button variant="contained" startIcon={<EditIcon />} onClick={() => setEditing(true)}>
            {t('labels.cardPage.edit')}
          </Button>
        )}
      </Stack>
      {/* On a phone (a scanned label) the facts come first, the art below them. */}
      <Stack direction={{ xs: 'column-reverse', md: 'row' }} spacing={3} alignItems="flex-start">
        <Box sx={{ width: { xs: 220, md: 300 }, alignSelf: { xs: 'center', md: 'flex-start' } }}>
          <CardImage src={card.imageUri ?? undefined} alt={card.name} foil={card.isFoil} sx={{ width: '100%', borderRadius: 2 }} />
        </Box>
        <Paper sx={{ flex: 1, width: '100%' }}>
          <Table size="small">
            <TableBody>
              {rows.map(([label, value]) => (
                <TableRow key={label}>
                  <TableCell component="th" sx={{ width: 160, color: 'text.secondary' }}>
                    {label}
                  </TableCell>
                  <TableCell>{value}</TableCell>
                </TableRow>
              ))}
              {card.tags.length > 0 && (
                <TableRow>
                  <TableCell component="th" sx={{ color: 'text.secondary' }}>
                    {t('labels.cardPage.tags')}
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {card.tags.map((tag) => (
                        <Chip key={tag} size="small" label={tag} />
                      ))}
                    </Stack>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Paper>
      </Stack>
      <CardEditDrawer cardId={editing ? cardId : null} onClose={() => setEditing(false)} />
    </Stack>
  );
}
