import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import {
  AppBar,
  Badge,
  BottomNavigation,
  BottomNavigationAction,
  Box,
  Paper,
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
import MoreHorizIcon from '@mui/icons-material/MoreHoriz';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import LogoutIcon from '@mui/icons-material/Logout';
import DashboardIcon from '@mui/icons-material/Dashboard';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import GridViewIcon from '@mui/icons-material/GridView';
import ChecklistIcon from '@mui/icons-material/Checklist';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import PointOfSaleIcon from '@mui/icons-material/PointOfSale';
import LocalShippingIcon from '@mui/icons-material/LocalShipping';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import FormatListBulletedIcon from '@mui/icons-material/FormatListBulleted';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import AdminPanelSettingsIcon from '@mui/icons-material/AdminPanelSettings';
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { usePermissions } from '../context/usePermissions';

const DRAWER_WIDTH = 200;

// Phones get a bottom tab bar: the first four of these sections the user can see, then "More" (the
// full drawer).
const BOTTOM_NAV = ['/', '/scan', '/collection', '/locations', '/sets', '/lists', '/sales'];
const BOTTOM_NAV_HEIGHT = 56;
const SECTION_ALIASES: Record<string, string[]> = {
  '/locations': ['/location/', '/binder/', '/audit/'],
  '/collection': ['/card/'],
};

// `labelKey` resolves against the `nav` namespace at render time. `perm` lists the view permission(s)
// that reveal the item — a user needs at least one. Items with no `perm` are always shown
// (Administration hosts self-service password change + the always-viewable Components tab).
// `badge` names a live count shown on the icon.
type NavBadge = 'scanBatches' | 'kioskQueue';

const NAV: { to: string; labelKey: string; icon: ReactNode; perm?: string[]; badge?: NavBadge }[] = [
  { to: '/', labelKey: 'dashboard', icon: <DashboardIcon />, perm: ['dashboard.view'] },
  { to: '/scan', labelKey: 'scan', icon: <PhotoCameraIcon />, perm: ['scan.view'], badge: 'scanBatches' },
  { to: '/collection', labelKey: 'collection', icon: <CollectionsBookmarkIcon />, perm: ['collection.view'] },
  { to: '/locations', labelKey: 'locations', icon: <GridViewIcon />, perm: ['locations.view'] },
  { to: '/sets', labelKey: 'sets', icon: <ChecklistIcon />, perm: ['sets.view'] },
  { to: '/inventory', labelKey: 'inventory', icon: <Inventory2Icon />, perm: ['inventory.view'] },
  { to: '/lists', labelKey: 'lists', icon: <FormatListBulletedIcon />, perm: ['lists.view'] },
  { to: '/trades', labelKey: 'trades', icon: <SwapHorizIcon />, perm: ['trades.view'] },
  { to: '/import', labelKey: 'import', icon: <UploadFileIcon />, perm: ['import.run'] },
  { to: '/sales', labelKey: 'sales', icon: <PointOfSaleIcon />, perm: ['sales.orders.view', 'sales.customers.view', 'sales.listings.view', 'sales.kiosk.view'], badge: 'kioskQueue' },
  { to: '/ship', labelKey: 'ship', icon: <LocalShippingIcon />, perm: ['sales.orders.ship', 'sales.orders.edit'] },
  { to: '/settings', labelKey: 'administration', icon: <AdminPanelSettingsIcon /> },
];

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const location = useLocation();
  const { game, setGame } = useGame();
  const qc = useQueryClient();
  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const { canAny, isAdmin } = usePermissions();
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'));
  const isPhone = useMediaQuery(theme.breakpoints.down('sm'));
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

  // Re-check permissions when navigating so an admin's change to this user (applied server-side
  // immediately) is reflected in the nav/UI within a click, without a reload.
  useEffect(() => {
    qc.invalidateQueries({ queryKey: ['auth-status'] });
  }, [location.pathname, qc]);

  const visibleNav = NAV.filter((item) => !item.perm || canAny(...item.perm));

  // Background scan batches nobody has opened yet (watched scan folders) — the Scan item's badge.
  const batchCount = useQuery({
    queryKey: ['scan-batches-count'],
    queryFn: api.scanBatchCount,
    enabled: canAny('scan.view'),
    refetchInterval: 20_000,
    refetchIntervalInBackground: false,
  });
  // Kiosk pick lists customers sent to the counter that nobody has started picking — the Sales item's badge.
  const kioskCount = useQuery({
    queryKey: ['kiosk-queue-count'],
    queryFn: api.kioskQueueCount,
    enabled: canAny('sales.kiosk.view'),
    refetchInterval: 20_000,
    refetchIntervalInBackground: false,
  });
  const badges = { scanBatches: batchCount.data?.unclaimed ?? 0, kioskQueue: kioskCount.data?.queued ?? 0 };

  // A section also owns its detail pages (a location's cards and binder, a card's page).
  const isActive = (to: string) =>
    to === '/'
      ? location.pathname === '/'
      : [to, ...(SECTION_ALIASES[to] ?? [])].some((p) => location.pathname.startsWith(p));
  const navIcon = (item: { icon: ReactNode; badge?: NavBadge }) =>
    item.badge ? (
      <Badge badgeContent={badges[item.badge]} color="secondary" max={99}>
        {item.icon}
      </Badge>
    ) : (
      item.icon
    );

  const bottomNav = BOTTOM_NAV.map((to) => visibleNav.find((n) => n.to === to))
    .filter((n) => n !== undefined)
    .slice(0, 4);
  const bottomValue = bottomNav.find((n) => isActive(n.to))?.to ?? 'more';

  const navItem = (item: { to: string; labelKey: string; icon: ReactNode; badge?: NavBadge }) => {
    const selected = isActive(item.to);
    return (
      <ListItemButton
        key={item.to}
        component={RouterLink}
        to={item.to}
        selected={selected}
        onClick={() => setMobileOpen(false)}
      >
        <ListItemIcon sx={{ minWidth: 40 }}>{navIcon(item)}</ListItemIcon>
        <ListItemText primary={t(`nav.${item.labelKey}`)} />
      </ListItemButton>
    );
  };

  // Main sections at the top; Help is pinned to the bottom of the drawer (the paper is a flex column).
  const navList = (
    <>
      <List>{visibleNav.map(navItem)}</List>
      <Box sx={{ flexGrow: 1 }} />
      <Divider />
      <List>{navItem({ to: '/help', labelKey: 'help', icon: <HelpOutlineIcon /> })}</List>
    </>
  );

  return (
    <Box sx={{ display: 'flex' }}>
      {/* Above the permanent desktop sidebar; below that, under drawers (card editor, menu) like any page. */}
      <AppBar position="fixed" sx={{ zIndex: (t) => (isDesktop ? t.zIndex.drawer + 1 : t.zIndex.appBar) }}>
        <Toolbar variant="dense">
          {!isDesktop && !isPhone && (
            <IconButton
              color="inherit"
              edge="start"
              aria-label={t('nav.openNavigation')}
              onClick={() => setMobileOpen((v) => !v)}
              sx={{ mr: 1 }}
            >
              <MenuIcon />
            </IconButton>
          )}
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {t('common.app.name')}
          </Typography>
          <FormControl size="small" sx={{ minWidth: { xs: 130, sm: 200 } }}>
            <Select
              value={game ?? '__all__'}
              onChange={(e) => setGame(e.target.value === '__all__' ? undefined : e.target.value)}
              sx={{ color: 'inherit', '.MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' } }}
            >
              <MenuItem value="__all__">{t('nav.allGames')}</MenuItem>
              {gamesQuery.data?.map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <IconButton
            color="inherit"
            aria-label={t('nav.account')}
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
                  {t('nav.signedInAs')} <strong>{username}</strong>
                </Typography>
              </MenuItem>
            )}
            {username && <Divider />}
            <MenuItem component={RouterLink} to="/account" onClick={() => setAccountAnchor(null)}>
              <ListItemIcon>
                <ManageAccountsIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>{t('nav.myAccount')}</ListItemText>
            </MenuItem>
            {isAdmin && (
              <MenuItem
                component={RouterLink}
                to="/settings?tab=users"
                onClick={() => setAccountAnchor(null)}
              >
                <ListItemIcon>
                  <AdminPanelSettingsIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>{t('nav.manageUsers')}</ListItemText>
              </MenuItem>
            )}
            <MenuItem disabled={logout.isPending} onClick={() => logout.mutate()}>
              <ListItemIcon>
                <LogoutIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>{t('nav.signOut')}</ListItemText>
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
          sx={{ [`& .MuiDrawer-paper`]: { width: DRAWER_WIDTH, boxSizing: 'border-box', pb: 'env(safe-area-inset-bottom)' } }}
        >
          {navList}
        </Drawer>
      )}

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          p: { xs: 1.5, sm: 3 },
          // Room for the bottom tab bar (and the phone's home indicator under it).
          pb: { xs: `calc(${BOTTOM_NAV_HEIGHT + 16}px + env(safe-area-inset-bottom))`, sm: 3 },
          minHeight: '100vh',
          width: 0,
        }}
      >
        <Toolbar variant="dense" />
        {children}
      </Box>

      {isPhone && (
        <Paper
          elevation={8}
          square
          sx={{
            position: 'fixed',
            left: 0,
            right: 0,
            bottom: 0,
            zIndex: (t) => t.zIndex.appBar,
            pb: 'env(safe-area-inset-bottom)',
          }}
        >
          <BottomNavigation value={bottomValue} showLabels sx={{ height: BOTTOM_NAV_HEIGHT }}>
            {bottomNav.map((item) => (
              <BottomNavigationAction
                key={item.to}
                value={item.to}
                component={RouterLink}
                to={item.to}
                label={t(`nav.${item.labelKey}`)}
                icon={navIcon(item)}
                sx={{ minWidth: 0, px: 0.5 }}
              />
            ))}
            <BottomNavigationAction
              value="more"
              label={t('nav.more')}
              icon={<MoreHorizIcon />}
              onClick={() => setMobileOpen(true)}
              sx={{ minWidth: 0, px: 0.5 }}
            />
          </BottomNavigation>
        </Paper>
      )}
    </Box>
  );
}
