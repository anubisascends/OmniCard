import { Route, Routes } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AppShell } from './components/AppShell';
import { RequirePermission } from './components/RequirePermission';
import { DashboardPage } from './pages/DashboardPage';
import { CollectionPage } from './pages/CollectionPage';
import { LocationsPage } from './pages/LocationsPage';
import { LocationDetailPage } from './pages/LocationDetailPage';
import { BinderPage } from './pages/BinderPage';
import { SetsPage } from './pages/SetsPage';
import { SalesPage } from './pages/SalesPage';
import { InventoryPage } from './pages/InventoryPage';
import { ImportPage } from './pages/ImportPage';
import { ScanPage } from './pages/ScanPage';
import { ListsPage } from './pages/ListsPage';
import { TradesPage } from './pages/TradesPage';
import { SettingsPage } from './pages/SettingsPage';
import { PlaceholderPage } from './pages/PlaceholderPage';

export function App() {
  const { t } = useTranslation();
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<RequirePermission anyOf={['dashboard.view']}><DashboardPage /></RequirePermission>} />
        <Route path="/scan" element={<RequirePermission anyOf={['scan.view']}><ScanPage /></RequirePermission>} />
        <Route path="/collection" element={<RequirePermission anyOf={['collection.view']}><CollectionPage /></RequirePermission>} />
        <Route path="/locations" element={<RequirePermission anyOf={['locations.view']}><LocationsPage /></RequirePermission>} />
        <Route path="/location/:id" element={<RequirePermission anyOf={['locations.view']}><LocationDetailPage /></RequirePermission>} />
        <Route path="/binder/:id" element={<RequirePermission anyOf={['binder.view', 'binder.edit']}><BinderPage /></RequirePermission>} />
        <Route path="/sets" element={<RequirePermission anyOf={['sets.view']}><SetsPage /></RequirePermission>} />
        <Route path="/inventory" element={<RequirePermission anyOf={['inventory.view']}><InventoryPage /></RequirePermission>} />
        <Route path="/import" element={<RequirePermission anyOf={['import.run']}><ImportPage /></RequirePermission>} />
        <Route path="/lists" element={<RequirePermission anyOf={['lists.view']}><ListsPage /></RequirePermission>} />
        <Route path="/trades" element={<RequirePermission anyOf={['trades.view']}><TradesPage /></RequirePermission>} />
        <Route path="/sales" element={<RequirePermission anyOf={['sales.orders.view', 'sales.customers.view', 'sales.listings.view']}><SalesPage /></RequirePermission>} />
        {/* Administration is always reachable: it hosts self-service password change + the
            always-viewable Components tab. Individual tabs gate themselves. */}
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<PlaceholderPage title={t('common.notFound')} />} />
      </Routes>
    </AppShell>
  );
}
