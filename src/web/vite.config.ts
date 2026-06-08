import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // @/foo → src/foo. Mirrors the tsconfig.app.json paths entry so both
      // the TypeScript compiler and Vite's runtime resolver agree.
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    // Proxy /api → ASP.NET backend during dev so the browser can call the
    // API from the same origin (avoids CORS + cookie issues with the
    // httpOnly JWT cookie pattern).
    proxy: {
      '/api': {
        target: 'http://localhost:5124',
        changeOrigin: true,
      },
    },
  },
})
