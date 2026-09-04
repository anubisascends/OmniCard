import { type ReactNode } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import {
  AppBar,
  Box,
  Drawer,
  FormControl,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  MenuItem,
  Select,
  Toolbar,
  Typography,
} from '@mui/material';
import DashboardIcon from '@mui/icons-material/Dashboard';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import GridViewIcon from '@mui/icons-material/GridView';
import ChecklistIcon from '@mui/icons-material/Checklist';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import PointOfSaleIcon from '@mui/icons-material/PointOfSale';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

const DRAWER_WIDTH = 200;

const NAV: { to: string; label: string; icon: ReactNode }[] = [
  { to: '/', label: 'Dashboard', icon: <DashboardIcon /> },
  { to: '/collection', label: 'Collection', icon: <CollectionsBookmarkIcon /> },
  { to: '/locations', label: 'Locations', icon: <GridViewIcon /> },
  { to: '/sets', label: 'Sets', icon: <ChecklistIcon /> },
  { to: '/inventory', label: 'Inventory', icon: <Inventory2Icon /> },
  { to: '/import', label: 'Import', icon: <UploadFileIcon /> },
  { to: '/sales', label: 'Sales', icon: <PointOfSaleIcon /> },
];

export function AppShell({ children }: { children: ReactNode }) {
  const location = useLocation();
  const { game, setGame } = useGame();
  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });

  return (
    <Box sx={{ display: 'flex' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar variant="dense">
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            OmniCard
          </Typography>
          <FormControl size="small" sx={{ minWidth: 200 }}>
            <Select
              value={game ?? '__all__'}
              onChange={(e) => setGame(e.target.value === '__all__' ? undefined : e.target.value)}
              sx={{ color: 'inherit', '.MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' } }}
            >
              <MenuItem value="__all__">All Games</MenuItem>
              {gamesQuery.data?.map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        sx={{
          width: DRAWER_WIDTH,
          flexShrink: 0,
          [`& .MuiDrawer-paper`]: { width: DRAWER_WIDTH, boxSizing: 'border-box' },
        }}
      >
        <Toolbar variant="dense" />
        <List>
          {NAV.map((item) => {
            const selected =
              item.to === '/' ? location.pathname === '/' : location.pathname.startsWith(item.to);
            return (
              <ListItemButton
                key={item.to}
                component={RouterLink}
                to={item.to}
                selected={selected}
              >
                <ListItemIcon sx={{ minWidth: 40 }}>{item.icon}</ListItemIcon>
                <ListItemText primary={item.label} />
              </ListItemButton>
            );
          })}
        </List>
      </Drawer>

      <Box component="main" sx={{ flexGrow: 1, p: 3, minHeight: '100vh' }}>
        <Toolbar variant="dense" />
        {children}
      </Box>
    </Box>
  );
}
