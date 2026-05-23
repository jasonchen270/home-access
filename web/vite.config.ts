import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Proxy /api to the API dev server so requests are same-origin (cookies, no CORS preflight).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:5000", changeOrigin: true },
    },
  },
});
