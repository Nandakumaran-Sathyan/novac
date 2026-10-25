import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The API's CORS policy allows exactly this origin, so fail loudly instead of
    // silently moving to another port if 5173 is taken.
    port: 5173,
    strictPort: true,
  },
})
