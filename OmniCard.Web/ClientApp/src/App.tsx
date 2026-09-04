import { Route, Routes } from 'react-router-dom';
import { AppShell } from './components/AppShell';
import { DashboardPage } from './pages/DashboardPage';
import { CollectionPage } from './pages/CollectionPage';
import { LocationsPage } from './pages/LocationsPage';
import { LocationDetailPage } from './pages/LocationDetailPage';
import { BinderPage } from './pages/BinderPage';
import { SetsPage } from './pages/SetsPage';
import { PlaceholderPage } from './pages/PlaceholderPage';

export function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/collection" element={<CollectionPage />} />
        <Route path="/locations" element={<LocationsPage />} />
        <Route path="/location/:id" element={<LocationDetailPage />} />
        <Route path="/binder/:id" element={<BinderPage />} />
        <Route path="/sets" element={<SetsPage />} />
        <Route path="/import" element={<PlaceholderPage title="Import" />} />
        <Route path="/sales" element={<PlaceholderPage title="Sales" />} />
        <Route path="*" element={<PlaceholderPage title="Not Found" />} />
      </Routes>
    </AppShell>
  );
}
