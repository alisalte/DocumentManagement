import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Unit tests live in src; e2e/ belongs to Playwright.
  test: {
    include: ['src/**/*.{test,spec}.ts'],
  },
  server: {
    port: 5173,
    // The API allows this origin in development (Dms:Cors:Origins).
    proxy: {
      '/api': {
        target: process.env.VITE_API_BASE_URL ?? 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
});
