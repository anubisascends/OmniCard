import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box,
  Button,
  Checkbox,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Popover,
  Tooltip,
} from '@mui/material';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ViewWeekIcon from '@mui/icons-material/ViewWeek';
import type { ViewState } from '../../lib/savedViews';

export interface ColumnChoice {
  field: string;
  label: string;
  /** False for columns that must always show (the card name). */
  hideable: boolean;
}

/**
 * "Columns" button for the card grid: show/hide each column and move it up/down (the grid's community
 * edition has no drag-reordering). Changes go into the page's view layout; an order that matches the
 * default is stored as "no custom order" so it doesn't count as a change.
 */
export function ColumnLayoutMenu({
  columns,
  defaultOrder,
  hidden,
  onChange,
}: {
  /** Every column, in the order currently shown. */
  columns: ColumnChoice[];
  defaultOrder: string[];
  hidden: string[];
  onChange: (patch: Pick<Partial<ViewState>, 'hiddenColumns' | 'columnOrder'>) => void;
}) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const hiddenSet = new Set(hidden);

  const toggle = (field: string) =>
    onChange({ hiddenColumns: hiddenSet.has(field) ? hidden.filter((f) => f !== field) : [...hidden, field] });

  const move = (index: number, delta: number) => {
    const order = columns.map((c) => c.field);
    const [field] = order.splice(index, 1);
    order.splice(index + delta, 0, field);
    const isDefault = order.length === defaultOrder.length && order.every((f, i) => f === defaultOrder[i]);
    onChange({ columnOrder: isDefault ? [] : order });
  };

  return (
    <>
      <Button size="small" startIcon={<ViewWeekIcon />} onClick={(e) => setAnchor(e.currentTarget)}>
        {t('collection.savedViews.columns.button')}
      </Button>
      <Popover
        open={!!anchor}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      >
        <List dense sx={{ minWidth: 260 }}>
          {columns.map((c, i) => (
            <ListItem
              key={c.field}
              secondaryAction={
                <Box>
                  <Tooltip title={t('collection.savedViews.columns.moveUp')}>
                    <span>
                      <IconButton size="small" disabled={i === 0} onClick={() => move(i, -1)}>
                        <ArrowUpwardIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                  <Tooltip title={t('collection.savedViews.columns.moveDown')}>
                    <span>
                      <IconButton size="small" disabled={i === columns.length - 1} onClick={() => move(i, 1)}>
                        <ArrowDownwardIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                </Box>
              }
              sx={{ pr: 11 }}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>
                <Checkbox
                  edge="start"
                  size="small"
                  checked={!hiddenSet.has(c.field)}
                  disabled={!c.hideable}
                  onChange={() => toggle(c.field)}
                  inputProps={{ 'aria-label': c.label }}
                />
              </ListItemIcon>
              <ListItemText primary={c.label} />
            </ListItem>
          ))}
        </List>
        <Divider />
        <Box sx={{ p: 1, display: 'flex', justifyContent: 'flex-end' }}>
          <Button size="small" onClick={() => onChange({ hiddenColumns: [], columnOrder: [] })}>
            {t('collection.savedViews.columns.reset')}
          </Button>
        </Box>
      </Popover>
    </>
  );
}
