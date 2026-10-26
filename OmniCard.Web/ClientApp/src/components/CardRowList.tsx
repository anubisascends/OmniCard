import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Checkbox, Chip, IconButton, LinearProgress, Stack, Typography } from '@mui/material';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import type { CardDto, CardGroupDto } from '../api/types';
import { CardImage } from './CardImage';
import { LanguageChip } from '../lib/cardLanguages';
import { useFormatters } from '../i18n/format';
import { LONG_PRESS_SX, useLongPress } from '../lib/responsive';
import type { CardHover } from './CardHoverPreview';

type Row = CardDto & { group?: CardGroupDto };

/**
 * The card table's phone layout: one compact row per card (art, name, set/number, condition, foil,
 * language, listing status, quantity and price) instead of a wide grid. Tapping a row opens it (or
 * toggles it in Select mode), long-pressing shows its art; group headers from server-side row grouping
 * render as tappable bars. Paged like the grid, with a pager under the list.
 */
export function CardRowList({
  rows,
  loading,
  showLocation,
  selectMode,
  isSelected,
  onToggleSelect,
  onOpen,
  onToggleGroup,
  renderGroupHeader,
  renderGroupCheckbox,
  onPreview,
  page,
  pageSize,
  total,
  onPageChange,
}: {
  rows: Row[];
  loading: boolean;
  showLocation: boolean;
  selectMode: boolean;
  isSelected: (id: number) => boolean;
  onToggleSelect: (row: CardDto) => void;
  onOpen: (row: CardDto) => void;
  onToggleGroup: (groupId: string) => void;
  renderGroupHeader: (g: CardGroupDto) => ReactNode;
  renderGroupCheckbox: (g: CardGroupDto) => ReactNode;
  onPreview: (hover: CardHover) => void;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const longPress = useLongPress();
  const lastPage = Math.max(0, Math.ceil(total / pageSize) - 1);
  const goTo = (p: number) => {
    onPageChange(p);
    window.scrollTo({ top: 0 });
  };

  return (
    <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 2, overflow: 'hidden' }}>
      <Box sx={{ height: 4, mb: '-4px', position: 'relative', zIndex: 1 }}>{loading && <LinearProgress />}</Box>
      {rows.length === 0 && !loading && (
        <Typography color="text.secondary" sx={{ p: 2, textAlign: 'center' }}>
          {t('collection.mobile.noCards')}
        </Typography>
      )}
      {rows.map((row) =>
        row.group ? (
          <Box
            key={row.id}
            onClick={() => onToggleGroup(row.group!.id)}
            sx={{
              display: 'flex',
              alignItems: 'center',
              minHeight: 44,
              px: 1,
              bgcolor: 'action.selected',
              borderBottom: 1,
              borderColor: 'divider',
              cursor: 'pointer',
              overflow: 'hidden',
            }}
          >
            {selectMode && renderGroupCheckbox(row.group)}
            <Box sx={{ minWidth: 0, flexGrow: 1, '& > *': { flexWrap: 'wrap' } }}>{renderGroupHeader(row.group)}</Box>
          </Box>
        ) : (
          <Box
            key={row.id}
            onClick={() => (selectMode ? onToggleSelect(row) : onOpen(row))}
            {...longPress((el) => row.imageUri && onPreview({ el, url: row.imageUri, foil: row.isFoil, touch: true }))}
            sx={{
              ...LONG_PRESS_SX,
              display: 'flex',
              alignItems: 'center',
              gap: 1.25,
              px: 1,
              py: 0.75,
              borderBottom: 1,
              borderColor: 'divider',
              cursor: 'pointer',
              bgcolor: isSelected(row.id) ? 'action.selected' : undefined,
              '&:active': { bgcolor: 'action.hover' },
            }}
          >
            {selectMode && (
              <Checkbox
                size="small"
                checked={isSelected(row.id)}
                sx={{ p: 0.5, mr: -0.5 }}
                inputProps={{ 'aria-label': row.name }}
                onClick={(e) => e.stopPropagation()}
                onChange={() => onToggleSelect(row)}
              />
            )}
            <Box sx={{ width: 40, height: 56, flexShrink: 0, borderRadius: 0.75, overflow: 'hidden', bgcolor: 'action.hover' }}>
              {row.imageUri && (
                <CardImage
                  src={row.imageUri}
                  foil={row.isFoil}
                  loading="lazy"
                  draggable={false}
                  wrapperSx={{ width: '100%', height: '100%' }}
                  sx={{ width: '100%', height: '100%', objectFit: 'cover' }}
                />
              )}
            </Box>
            <Box sx={{ minWidth: 0, flexGrow: 1 }}>
              <Typography variant="body2" fontWeight={500} noWrap>
                {row.name}
                {row.stackedIds.length > 1 && (
                  <Typography component="span" variant="caption" color="text.secondary">
                    {' · '}
                    {t('collection.printings', { count: row.stackedIds.length })}
                  </Typography>
                )}
              </Typography>
              <Stack direction="row" spacing={0.5} alignItems="center" sx={{ minWidth: 0, mt: 0.25 }}>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ minWidth: 0 }}>
                  {[row.setCode?.toUpperCase(), row.number && `#${row.number}`, row.condition].filter(Boolean).join(' · ')}
                </Typography>
                <LanguageChip language={row.language} />
                {row.isFoil && <Chip size="small" label={t('common.labels.foil')} sx={{ height: 18, fontSize: 11 }} />}
                {row.listingStatus && (
                  <Chip
                    size="small"
                    color="warning"
                    variant="outlined"
                    sx={{ height: 18, fontSize: 11 }}
                    label={t(`common.listingStatus.${row.listingStatus}`, { defaultValue: row.listingStatus })}
                  />
                )}
              </Stack>
              {showLocation && row.containerName && (
                <Typography variant="caption" color="text.secondary" noWrap component="div">
                  {row.containerName}
                </Typography>
              )}
            </Box>
            <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
              <Typography variant="body2" fontWeight={500}>
                {row.marketPrice ? fmt.money(row.marketPrice) : '—'}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {t('collection.mobile.quantity', { count: row.quantity, countText: fmt.number(row.quantity) })}
              </Typography>
            </Box>
          </Box>
        ),
      )}
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="flex-end"
        spacing={1}
        sx={{ px: 1, py: 0.5 }}
      >
        <Typography variant="body2" color="text.secondary">
          {t('collection.mobile.range', {
            from: fmt.number(total ? page * pageSize + 1 : 0),
            to: fmt.number(Math.min(total, (page + 1) * pageSize)),
            total: fmt.number(total),
          })}
        </Typography>
        <IconButton aria-label={t('collection.mobile.previousPage')} disabled={page === 0} onClick={() => goTo(page - 1)}>
          <ChevronLeftIcon />
        </IconButton>
        <IconButton aria-label={t('collection.mobile.nextPage')} disabled={page >= lastPage} onClick={() => goTo(page + 1)}>
          <ChevronRightIcon />
        </IconButton>
      </Stack>
    </Box>
  );
}
