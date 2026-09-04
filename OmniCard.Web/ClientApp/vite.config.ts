import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev server proxies API/SignalR/scan-image traffic to the ASP.NET Core backend running on :5000,
// so the SPA can be developed against the real API with no CORS setup. In production the built
// static files are served by ASP.NET Core itself (see Phase 7 deployment).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5000', changeOrigin: true, ws: true },
      '/scans': { target: 'http://localhost:5000', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5000', changeOrigin: true },
    },
  },
  build: {
    // Emitted into the web project's wwwroot so ASP.NET Core can serve it.
    outDir: '../wwwroot/app',
    emptyOutDir: true,
  },
  base: '/app/',
});
