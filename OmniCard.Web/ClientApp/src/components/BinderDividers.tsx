import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box,
  Button,
  ButtonBase,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Stack,
  TextField,
  Tooltip,
  Typography,
  alpha,
  useTheme,
  type SxProps,
  type Theme,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import CheckIcon from '@mui/icons-material/Check';
import type { BinderDividerDto } from '../api/types';
import { useFormatters } from '../i18n/format';

/** Tab colors, keyed like OmniCard.Shared.Binder.BinderDivider.Colors. */
export const DIVIDER_COLORS: Record<string, string> = {
  red: '#d32f2f',
  orange: '#ef6c00',
  yellow: '#f9a825',
  green: '#2e7d32',
  teal: '#00897b',
  blue: '#1565c0',
  purple: '#6a1b9a',
  pink: '#c2185b',
  brown: '#6d4c41',
  gray: '#616161',
};
export const MAX_DIVIDER_NAME = 60;

const tabColor = (theme: Theme, color?: string | null) => (color && DIVIDER_COLORS[color]) || theme.palette.primary.main;

/** The divider whose section `page` is in: the last one at or before it (null before the first divider). */
export const sectionOf = (dividers: BinderDividerDto[], page: number): BinderDividerDto | null => {
  let found: BinderDividerDto | null = null;
  for (const d of dividers) if (d.page <= page) found = d;
  return found;
};

const TAB_GAP = 4; // px between tabs (theme.spacing(0.5))

/** The binder's dividers as index tabs, in binder order. Tabs that don't fit on one line wrap onto more
 *  rows (no scrolling), and they stay put: selecting a tab only fills it with its color, so no tab ever
 *  changes size or row. Arrow keys / Home / End move between tabs (and turn to them); a click, Enter or
 *  Space turns to the divider and hands the keyboard back to the pages (`how: 'activate'`). */
export function BinderDividerTabs({
  dividers,
  activeId,
  onSelect,
  editable,
  onRemove,
  onTabContextMenu,
}: {
  dividers: BinderDividerDto[];
  activeId: number | null;
  onSelect: (d: BinderDividerDto, how: 'activate' | 'arrow') => void;
  /** Edit mode: each tab gets a remove (×) button and a right-click menu. */
  editable: boolean;
  onRemove: (d: BinderDividerDto) => void;
  onTabContextMenu: (e: React.MouseEvent, d: BinderDividerDto) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const theme = useTheme();
  const buttonRefs = useRef(new Map<number, HTMLElement>());
  const activeIndex = dividers.findIndex((d) => d.id === activeId);
  // Roving focus: Tab reaches the active tab (or the first one); the arrows move between them.
  const focusIndex = activeIndex >= 0 ? activeIndex : 0;

  const onKeyDown = (e: React.KeyboardEvent, index: number) => {
    let next: number | null = null;
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = (index + 1) % dividers.length;
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = (index - 1 + dividers.length) % dividers.length;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = dividers.length - 1;
    if (next == null) return;
    e.preventDefault();
    e.stopPropagation();
    buttonRefs.current.get(dividers[next].id)?.focus();
    onSelect(dividers[next], 'arrow');
  };

  return (
    <Box
      role="tablist"
      aria-label={t('binder.dividers.stripLabel')}
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'flex-end',
        columnGap: `${TAB_GAP}px`,
        rowGap: 0.25,
        px: 0.5,
        pt: 0.5,
        borderBottom: 2,
        borderColor: 'divider',
      }}
    >
      {dividers.map((d, index) => {
        const bg = tabColor(theme, d.color);
        const active = d.id === activeId;
        const fg = active ? theme.palette.getContrastText(bg) : theme.palette.text.primary;
        return (
          <Box key={d.id} sx={{ position: 'relative', flexShrink: 0, display: 'flex', maxWidth: '100%' }}>
            <Tooltip describeChild title={t('binder.dividers.tabTooltip', { name: d.name, page: fmt.number(d.page) })}>
              <ButtonBase
                ref={(el: HTMLButtonElement | null) => {
                  if (el) buttonRefs.current.set(d.id, el);
                  else buttonRefs.current.delete(d.id);
                }}
                role="tab"
                aria-selected={active}
                tabIndex={index === focusIndex ? 0 : -1}
                onClick={() => onSelect(d, 'activate')}
                onKeyDown={(e) => onKeyDown(e, index)}
                onContextMenu={editable ? (e) => onTabContextMenu(e, d) : undefined}
                sx={{
                  maxWidth: 240,
                  minHeight: 34,
                  pl: 1.5,
                  pr: editable ? 3.5 : 1.5,
                  borderTopLeftRadius: 8,
                  borderTopRightRadius: 8,
                  borderTop: `3px solid ${bg}`,
                  bgcolor: active ? bg : alpha(bg, 0.16),
                  color: fg,
                  boxShadow: active ? 3 : 0,
                  transition: theme.transitions.create(['background-color', 'box-shadow']),
                  '&:hover': { bgcolor: active ? bg : alpha(bg, 0.3) },
                  '&.Mui-focusVisible': { outline: `2px solid ${theme.palette.text.primary}`, outlineOffset: 1 },
                }}
              >
                {/* The name is laid over a hidden bold copy of itself, so the tab is always as wide as its
                    bold (selected) look and selecting it never re-wraps the rows. */}
                <Box component="span" sx={{ display: 'inline-grid', gridTemplateColumns: 'minmax(0, auto)', minWidth: 0 }}>
                  <Typography component="span" variant="body2" noWrap sx={{ gridArea: '1 / 1', fontWeight: active ? 700 : 500 }}>
                    {d.name}
                  </Typography>
                  <Typography component="span" variant="body2" noWrap aria-hidden sx={{ gridArea: '1 / 1', fontWeight: 700, visibility: 'hidden' }}>
                    {d.name}
                  </Typography>
                </Box>
                <Typography variant="caption" sx={{ ml: 0.75, opacity: 0.75, flexShrink: 0 }}>
                  {t('binder.dividers.tabPage', { page: fmt.number(d.page) })}
                </Typography>
              </ButtonBase>
            </Tooltip>
            {editable && (
              <Tooltip title={t('binder.dividers.remove')}>
                <IconButton
                  size="small"
                  aria-label={t('binder.dividers.removeNamed', { name: d.name })}
                  onClick={() => onRemove(d)}
                  sx={{ position: 'absolute', right: 2, top: '50%', transform: 'translateY(-50%)', p: 0.25, color: fg }}
                >
                  <CloseIcon sx={{ fontSize: 14 }} />
                </IconButton>
              </Tooltip>
            )}
          </Box>
        );
      })}
    </Box>
  );
}

/** Where dividers sit on the page: a colored spine between two facing pages (stacked pages on a phone,
 *  or `horizontal`: a band across the top of a page). */
export function BinderDividerMarker({
  dividers,
  horizontal,
  sx,
}: {
  dividers: BinderDividerDto[];
  horizontal?: boolean;
  sx?: SxProps<Theme>;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  // A spine on wide screens (pages side by side), a band when pages stack or `horizontal` is asked for.
  const pick = (spine: string | number, band: string | number) => (horizontal ? band : { xs: band, sm: spine });
  return (
    <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap" sx={{ flexShrink: 0, alignSelf: 'stretch', ...sx }}>
      {dividers.map((d) => {
        const bg = tabColor(theme, d.color);
        return (
          <Tooltip key={d.id} title={t('binder.dividers.markerTooltip', { name: d.name })}>
            <Box
              aria-label={t('binder.dividers.markerTooltip', { name: d.name })}
              sx={{
                bgcolor: bg,
                color: theme.palette.getContrastText(bg),
                borderRadius: 1,
                boxShadow: 2,
                fontWeight: 700,
                fontSize: 13,
                lineHeight: 1.6,
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                writingMode: pick('vertical-rl', 'horizontal-tb'),
                width: pick(28, 'auto'),
                px: pick(0, 1.25),
                py: pick(1.5, 0.25),
                // The name sits at the top of a spine (pages are tall), like a real divider's tab.
                textAlign: pick('start', 'center'),
              }}
            >
              {d.name}
            </Box>
          </Tooltip>
        );
      })}
    </Stack>
  );
}

/** Add / rename / recolor a divider: a name (required) and an optional tab color. */
export function BinderDividerDialog({
  open,
  title,
  initialName,
  initialColor,
  onClose,
  onSave,
}: {
  open: boolean;
  title: string;
  initialName?: string;
  initialColor?: string | null;
  onClose: () => void;
  onSave: (name: string, color: string | null) => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [name, setName] = useState(initialName ?? '');
  const [color, setColor] = useState<string | null>(initialColor ?? null);
  useEffect(() => {
    if (!open) return;
    setName(initialName ?? '');
    setColor(initialColor ?? null);
  }, [open, initialName, initialColor]);

  const trimmed = name.trim();
  const save = () => {
    if (trimmed) onSave(trimmed, color);
  };
  const swatches: (string | null)[] = [null, ...Object.keys(DIVIDER_COLORS)];

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <DialogTitle>{title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              autoFocus
              label={t('common.labels.name')}
              placeholder={t('binder.dividers.namePlaceholder')}
              value={name}
              onChange={(e) => setName(e.target.value)}
              slotProps={{ htmlInput: { maxLength: MAX_DIVIDER_NAME } }}
            />
            <Box>
              <Typography variant="caption" color="text.secondary">
                {t('binder.dividers.color')}
              </Typography>
              <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }} role="radiogroup">
                {swatches.map((key) => {
                  const bg = tabColor(theme, key);
                  const selected = color === key;
                  const label = key ? t(`binder.dividers.colors.${key}`) : t('binder.dividers.defaultColor');
                  return (
                    <Tooltip key={key ?? 'default'} title={label}>
                      <ButtonBase
                        role="radio"
                        aria-checked={selected}
                        aria-label={label}
                        onClick={() => setColor(key)}
                        sx={{
                          width: 28,
                          height: 28,
                          borderRadius: '50%',
                          bgcolor: bg,
                          color: theme.palette.getContrastText(bg),
                          outline: selected ? `2px solid ${theme.palette.text.primary}` : 'none',
                          outlineOffset: 2,
                          '&.Mui-focusVisible': { outline: `2px solid ${theme.palette.text.secondary}` },
                        }}
                      >
                        {selected && <CheckIcon sx={{ fontSize: 16 }} />}
                      </ButtonBase>
                    </Tooltip>
                  );
                })}
              </Stack>
            </Box>
            <Typography variant="caption" color="text.secondary">
              {t('binder.dividers.help')}
            </Typography>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={!trimmed}>
            {t('common.actions.save')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
