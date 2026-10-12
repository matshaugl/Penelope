import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const root = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/Penelope.Host/wwwroot",
    emptyOutDir: true,
  },
  server: {
    fs: { allow: [path.resolve(root, "..")] },
    proxy: {
      "/api": "http://127.0.0.1:47321",
      "/session": "http://127.0.0.1:47321",
    },
  },
});
