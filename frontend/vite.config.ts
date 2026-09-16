import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Del 21 — plain Vite dev server on port 5173, the exact origin Program.cs's
// "AllowLocalDev" CORS policy already whitelisted back in Del 5 (see the
// comment above builder.Services.AddCors in src/Atlas.Api/Program.cs) —
// this project anticipated a React dev server here well before Del 21
// actually built one.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
});
