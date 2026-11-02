import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, IconButton, ListItemIcon, Menu, MenuItem, Paper, Stack, Tooltip, Typography } from '@mui/material';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import EditIcon from '@mui/icons-material/Edit';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import type { DashboardWidgetDto } from '../../api/types';
import { WidgetBody } from './WidgetBody';

/** The class react-grid-layout drags widgets by. */
export const DRAG_HANDLE_CLASS = 'dashboard-drag-handle';

interface WidgetCardProps {
  widget: DashboardWidgetDto;
  title: string;
  editing: boolean;
  /** Phones show widgets in a plain list: no drag handle. */
  draggable?: boolean;
  onEdit?: () => void;
  onDuplicate?: () => void;
  onRemove?: () => void;
}

/**
 * One widget's card. A stat shows its title inside the tile (like the classic totals); tables, charts
 * and notes get a heading row. While editing, a toolbar offers drag and edit, with duplicate and remove in its menu.
 */
export function WidgetCard({ widget, title, editing, draggable = true, onEdit, onDuplicate, onRemove }: WidgetCardProps) {
  const { t } = useTranslation();
  const isStat = widget.display === 'stat' && widget.type !== 'note';

  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);

  const tools = editing && (
    <Stack
      direction="row"
      spacing={0}
      sx={
        isStat
          ? // Over the tile's corner, so a narrow stat keeps its full width for the number.
            { position: 'absolute', top: 4, right: 4, bgcolor: 'background.paper', borderRadius: 1, boxShadow: 1, zIndex: 1 }
          : { flexShrink: 0, ml: 'auto', mt: -0.5, mr: -0.5 }
      }
    >
      {draggable && (
        <Tooltip title={t('dashboard.widget.move')}>
          <IconButton size="small" className={DRAG_HANDLE_CLASS} sx={{ cursor: 'grab' }} aria-label={t('dashboard.widget.move')}>
            <DragIndicatorIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}
      <Tooltip title={t('dashboard.widget.edit')}>
        <IconButton size="small" onClick={onEdit} aria-label={t('dashboard.widget.edit')}>
          <EditIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <IconButton size="small" onClick={(e) => setMenuAnchor(e.currentTarget)} aria-label={t('dashboard.widget.more')}>
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu anchorEl={menuAnchor} open={!!menuAnchor} onClose={() => setMenuAnchor(null)}>
        <MenuItem
          onClick={() => {
            setMenuAnchor(null);
            onDuplicate?.();
          }}
        >
          <ListItemIcon>
            <ContentCopyIcon fontSize="small" />
          </ListItemIcon>
          {t('dashboard.widget.duplicate')}
        </MenuItem>
        <MenuItem
          onClick={() => {
            setMenuAnchor(null);
            onRemove?.();
          }}
        >
          <ListItemIcon>
            <DeleteOutlineIcon fontSize="small" />
          </ListItemIcon>
          {t('dashboard.widget.remove')}
        </MenuItem>
      </Menu>
    </Stack>
  );

  return (
    <Paper
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        p: isStat ? { xs: 1.5, sm: 2 } : 2,
        overflow: 'hidden',
        position: 'relative',
        outline: editing ? 1 : 0,
        outlineColor: 'divider',
        outlineStyle: 'dashed',
      }}
    >
      {isStat ? (
        <>
          <WidgetBody widget={widget} title={title} />
          {tools}
        </>
      ) : (
        <>
          <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1, minWidth: 0 }}>
            <Typography variant="h6" noWrap title={title} sx={{ minWidth: 0 }}>
              {title}
            </Typography>
            {tools}
          </Stack>
          <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', display: 'flex', flexDirection: 'column' }}>
            <WidgetBody widget={widget} title={title} />
          </Box>
        </>
      )}
    </Paper>
  );
}
