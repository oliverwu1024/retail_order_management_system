import path from 'node:path'
import { defineConfig } from 'vitest/config'
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
  // Vitest runs component + unit tests in a jsdom DOM. The setup file registers
  // jest-dom matchers and cleans the DOM between tests. Coverage (v8) excludes
  // generated/bootstrap files so the percentage reflects hand-written code.
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Vitest owns *.test.* under src; Playwright owns e2e/*.spec.ts (which import
    // @playwright/test and must NOT run under Vitest). Keeping the suffixes
    // distinct keeps the two runners from fighting over the same files.
    include: ['src/**/*.test.{ts,tsx}'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'lcov'],
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/**/*.test.{ts,tsx}',
        'src/test/**',
        'src/lib/api/schema.d.ts',
        'src/**/*.d.ts',
        'src/main.tsx',
        'src/vite-env.d.ts',
      ],
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
