import { defineConfig } from "vite";

export default defineConfig({
  server: {
    proxy: Object.fromEntries(
      ["/auth", "/accounts", "/tables", "/catalog", "/orders", "/health"].map(
        (path) => [
          path,
          {
            target: process.env.API_URL || "http://127.0.0.1:5080",
            changeOrigin: true,
          },
        ],
      ),
    ),
  },
});
