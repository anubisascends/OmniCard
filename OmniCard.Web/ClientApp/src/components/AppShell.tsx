import { useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import {
  AppBar,
  Box,
  Divider,
  Drawer,
  FormControl,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Select,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import MenuIcon from '@mui/icons-material/Menu';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import LogoutIcon from '@mui/icons-material/Logout';
import DashboardIcon from '@mui/icons-material/Dashboard';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import GridViewIcon from '@mui/icons-material/GridView';
import ChecklistIcon from '@mui/icons-material/Checklist';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import PointOfSaleIcon from '@mui/icons-material/PointOfSale';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import FormatListBulletedIcon from '@mui/icons-material/FormatListBulleted';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import AdminPanelSettingsIcon from '@mui/icons-material/AdminPanelSettings';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

const DRAWER_WIDTH = 200;

const NAV: { to: string; label: string; icon: ReactNode }[] = [
  { to: '/', label: 'Dashboard', icon: <DashboardIcon /> },
  { to: '/scan', label: 'Scan', icon: <PhotoCameraIcon /> },
  { to: '/collection', label: 'Collection', icon: <CollectionsBookmarkIcon /> },
  { to: '/locations', label: 'Locations', icon: <GridViewIcon /> },
  { to: '/sets', label: 'Sets', icon: <ChecklistIcon /> },
  { to: '/inventory', label: 'Inventory', icon: <Inventory2Icon /> },
  { to: '/lists', label: 'Lists', icon: <FormatListBulletedIcon /> },
  { to: '/trades', label: 'Trades', icon: <SwapHorizIcon /> },
  { to: '/import', label: 'Import', icon: <UploadFileIcon /> },
  { to: '/sales', label: 'Sales', icon: <PointOfSaleIcon /> },
  { to: '/settings', label: 'Administration', icon: <AdminPanelSettingsIcon /> },
];

export function AppShell({ children }: { children: ReactNode }) {
  const location = useLocation();
  const { game, setGame } = useGame();
  const qc = useQueryClient();
  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'));
  const [mobileOpen, setMobileOpen] = useState(false);
  const [accountAnchor, setAccountAnchor] = useState<null | HTMLElement>(null);

  const logout = useMutation({
    mutationFn: () => api.logout(),
    onSuccess: (status) => {
      setAccountAnchor(null);
      qc.setQueryData(['auth-status'], status);
    },
  });

  const username = authQuery.data?.username;

  const navList = (
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
            onClick={() => setMobileOpen(false)}
          >
            <ListItemIcon sx={{ minWidth: 40 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} />
          </ListItemButton>
        );
      })}
    </List>
  );

  return (
    <Box sx={{ display: 'flex' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar variant="dense">
          {!isDesktop && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label="Open navigation"
              onClick={() => setMobileOpen((v) => !v)}
              sx={{ mr: 1 }}
            >
              <MenuIcon />
            </IconButton>
          )}
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            OmniCard
          </Typography>
          <FormControl size="small" sx={{ minWidth: { xs: 130, sm: 200 } }}>
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

          <IconButton
            color="inherit"
            aria-label="Account"
            onClick={(e) => setAccountAnchor(e.currentTarget)}
            sx={{ ml: 0.5 }}
          >
            <AccountCircleIcon />
          </IconButton>
          <Menu
            anchorEl={accountAnchor}
            open={Boolean(accountAnchor)}
            onClose={() => setAccountAnchor(null)}
          >
            {username && (
              <MenuItem disabled sx={{ opacity: '1 !important' }}>
                <Typography variant="body2" color="text.secondary">
                  Signed in as <strong>{username}</strong>
                </Typography>
              </MenuItem>
            )}
            {username && <Divider />}
            <MenuItem
              component={RouterLink}
              to="/settings?tab=users"
              onClick={() => setAccountAnchor(null)}
            >
              <ListItemIcon>
                <AdminPanelSettingsIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>Account &amp; users</ListItemText>
            </MenuItem>
            <MenuItem disabled={logout.isPending} onClick={() => logout.mutate()}>
              <ListItemIcon>
                <LogoutIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>Sign out</ListItemText>
            </MenuItem>
          </Menu>
        </Toolbar>
      </AppBar>

      {isDesktop ? (
        <Drawer
          variant="permanent"
          sx={{
            width: DRAWER_WIDTH,
            flexShrink: 0,
            [`& .MuiDrawer-paper`]: { width: DRAWER_WIDTH, boxSizing: 'border-box' },
          }}
        >
          <Toolbar variant="dense" />
          {navList}
        </Drawer>
      ) : (
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{ [`& .MuiDrawer-paper`]: { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
        >
          <Toolbar variant="dense" />
          {navList}
        </Drawer>
      )}

      <Box component="main" sx={{ flexGrow: 1, p: { xs: 1.5, sm: 3 }, minHeight: '100vh', width: 0 }}>
        <Toolbar variant="dense" />
        {children}
      </Box>
    </Box>
  );
}
