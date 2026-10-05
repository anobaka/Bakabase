import { fileURLToPath } from "node:url";

import { defineConfig, mergeConfig } from "vite";

import baseConfig from "./vite.config";

// An explicit development-only server: production builds keep vite.config.ts and main.tsx.
export default defineConfig(({ command }) => {
  if (command !== "serve") throw new Error("The post-parser review harness is development-only.");
  const file = (name: string) =>
    fileURLToPath(new URL(`./src/qa/post-parser/${name}`, import.meta.url));

  return mergeConfig(baseConfig, {
    define: { "import.meta.env.VITE_POST_PARSER_QA": JSON.stringify("true") },
    resolve: {
      alias: [
        { find: "@/sdk/BApi", replacement: file("mockApi.ts") },
        { find: "@/components/SignalR/UIHubConnection", replacement: file("mockSignalR.tsx") },
      ],
    },
    optimizeDeps: { entries: ["qa-post-parser.html"] },
    server: { host: "127.0.0.1", port: 4317, strictPort: true, open: false },
  });
});
