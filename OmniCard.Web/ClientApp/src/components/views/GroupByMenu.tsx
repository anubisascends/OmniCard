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
  Typography,
} from '@mui/material';
import AccountTreeIcon from '@mui/icons-material/AccountTree';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';

/** Columns the card table can group rows by (grid field names), in menu order. Mirrors the server's
 *  `CollectionGrouping.Fields`. */
export const GROUPABLE_FIELDS = [
  'setCode',
  'rarity',
  'condition',
  'language',
  'isFoil',
  'game',
  'containerName',
  'listingStatus',
] as const;

/**
 * "Group by" button for the card grid: tick columns to group rows by them, and move the ticked ones
 * up/down to set the nesting (top = outermost group). Ticked columns are listed first, in group order;
 * ticking another adds it as the innermost level.
 */
export function GroupByMenu({
  fields,
  value,
  onChange,
}: {
  /** The groupable fields offered on this page. */
  fields: string[];
  /** The current grouping, outermost first. */
  value: string[];
  onChange: (groupColumns: string[]) => void;
}) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const label = (f: string) => t(`collection.grouping.fields.${f}`);
  const items = [...value, ...fields.filter((f) => !value.includes(f))];

  const toggle = (field: string) =>
    onChange(value.includes(field) ? value.filter((f) => f !== field) : [...value, field]);

  const move = (index: number, delta: number) => {
    const order = [...value];
    const [field] = order.splice(index, 1);
    order.splice(index + delta, 0, field);
    onChange(order);
  };

  return (
    <>
      <Button
        size="small"
        variant={value.length ? 'contained' : 'text'}
        startIcon={<AccountTreeIcon />}
        onClick={(e) => setAnchor(e.currentTarget)}
      >
        {value.length
          ? t('collection.grouping.buttonActive', { columns: value.map(label).join(' ▸ ') })
          : t('collection.grouping.button')}
      </Button>
      <Popover
        open={!!anchor}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      >
        <Typography variant="body2" color="text.secondary" sx={{ px: 2, pt: 1.5, maxWidth: 300 }}>
          {t('collection.grouping.hint')}
        </Typography>
        <List dense sx={{ minWidth: 280 }}>
          {items.map((f) => {
            const level = value.indexOf(f);
            return (
              <ListItem
                key={f}
                secondaryAction={
                  level >= 0 && (
                    <Box>
                      <Tooltip title={t('collection.grouping.moveOut')}>
                        <span>
                          <IconButton size="small" disabled={level === 0} onClick={() => move(level, -1)}>
                            <ArrowUpwardIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                      <Tooltip title={t('collection.grouping.moveIn')}>
                        <span>
                          <IconButton size="small" disabled={level === value.length - 1} onClick={() => move(level, 1)}>
                            <ArrowDownwardIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                    </Box>
                  )
                }
                sx={{ pr: 11 }}
              >
                <ListItemIcon sx={{ minWidth: 36 }}>
                  <Checkbox
                    edge="start"
                    size="small"
                    checked={level >= 0}
                    onChange={() => toggle(f)}
                    inputProps={{ 'aria-label': label(f) }}
                  />
                </ListItemIcon>
                <ListItemText
                  primary={label(f)}
                  secondary={level >= 0 ? t('collection.grouping.level', { level: level + 1 }) : undefined}
                />
              </ListItem>
            );
          })}
        </List>
        <Divider />
        <Box sx={{ p: 1, display: 'flex', justifyContent: 'flex-end' }}>
          <Button size="small" disabled={!value.length} onClick={() => onChange([])}>
            {t('collection.grouping.clear')}
          </Button>
        </Box>
      </Popover>
    </>
  );
}
