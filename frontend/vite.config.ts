import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Production build is emitted straight into the ASP.NET Core wwwroot so the
// single WebHost process serves everything (no Node in production).
// In development, API and SSE calls proxy to the WebHost on :5170.
// (5000 would be the natural choice, but macOS AirPlay Receiver squats on it.)
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../src/LumosPresenter.WebHost/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5170',
      '/events': 'http://localhost:5170',
      '/healthz': 'http://localhost:5170',
    },
  },
})
