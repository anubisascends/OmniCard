import { useTranslation } from 'react-i18next';
import {
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Button,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  ListSubheader,
} from '@mui/material';
import FunctionsIcon from '@mui/icons-material/Functions';
import TableRowsIcon from '@mui/icons-material/TableRows';
import PieChartIcon from '@mui/icons-material/PieChart';
import SearchIcon from '@mui/icons-material/Search';
import StarIcon from '@mui/icons-material/Star';
import BarChartIcon from '@mui/icons-material/BarChart';
import StorageIcon from '@mui/icons-material/Storage';
import ShowChartIcon from '@mui/icons-material/ShowChart';
import NotesIcon from '@mui/icons-material/Notes';
import type { ReactNode } from 'react';
import { PRESETS, type PresetSection, type WidgetPreset } from './widgetModel';

const ICONS: Record<string, ReactNode> = {
  metric: <FunctionsIcon />,
  breakdownTable: <TableRowsIcon />,
  breakdownChart: <PieChartIcon />,
  searchSummary: <SearchIcon />,
  searchCards: <StarIcon />,
  searchGroups: <BarChartIcon />,
  sql: <StorageIcon />,
  sqlOrders: <ShowChartIcon />,
  sqlAdded: <BarChartIcon />,
  note: <NotesIcon />,
};

const SECTIONS: PresetSection[] = ['totals', 'breakdowns', 'search', 'sql', 'other'];

/** The Add widget gallery: a starting point per kind of widget, opened in the editor next. */
export function AddWidgetDialog({
  open,
  canWriteSql,
  canSearchCollection,
  onPick,
  onClose,
}: {
  open: boolean;
  canWriteSql: boolean;
  canSearchCollection: boolean;
  onPick: (preset: WidgetPreset) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const available = PRESETS.filter(
    (p) => (p.needs !== 'sql' || canWriteSql) && (p.needs !== 'search' || canSearchCollection),
  );
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('dashboard.add.title')}</DialogTitle>
      <DialogContent dividers sx={{ p: 0 }}>
        <List dense disablePadding>
          {SECTIONS.filter((s) => available.some((p) => p.section === s)).map((section) => (
            <li key={section}>
              <ul style={{ padding: 0 }}>
                <ListSubheader>{t(`dashboard.add.sections.${section}`)}</ListSubheader>
                {available
                  .filter((p) => p.section === section)
                  .map((preset) => (
                    <ListItemButton key={preset.key} onClick={() => onPick(preset)}>
                      <ListItemIcon>{ICONS[preset.key]}</ListItemIcon>
                      <ListItemText
                        primary={t(`dashboard.add.presets.${preset.key}.name`)}
                        secondary={t(`dashboard.add.presets.${preset.key}.description`)}
                      />
                    </ListItemButton>
                  ))}
              </ul>
            </li>
          ))}
        </List>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
      </DialogActions>
    </Dialog>
  );
}
