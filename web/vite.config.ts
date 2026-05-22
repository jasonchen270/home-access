import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Vite proxies /api to the ASP.NET dev server so the browser sees same-origin
// requests (cookies "just work" without CORS preflight headaches in dev).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:5000", changeOrigin: true },
    },
  },
});
