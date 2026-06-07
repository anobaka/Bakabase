import react from '@vitejs/plugin-react-swc';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import monkey from 'vite-plugin-monkey';

const isDev = process.env.NODE_ENV !== 'production';

const cdnScriptUrl = 'https://cdn-public.anobaka.com/app/bakabase/scripts/bakabase.user.js';

const prodMatch = [
  'https://exhentai.org/*',
  'https://www.north-plus.net/*',
];

export default defineConfig({
  define: {
    __DEV__: JSON.stringify(isDev),
  },
  build: {
    rollupOptions: {
      output: {
        // Emit ONE self-contained chunk. Without this, rollup splits vendor
        // code (HeroUI pulls in internal dynamic imports), and vite-plugin-monkey
        // falls back to stitching the chunks together with SystemJS + a jsdelivr
        // `@require`. That breaks the userscript two ways: it needs jsdelivr to be
        // reachable, and — fatally — Tampermonkey's granted `GM_*` functions live
        // in the userscript's immediate scope, which SystemJS-executed module
        // factories can't see, so calls throw "GM_getValue is not a function".
        // A single inlined chunk keeps everything in scope and offline-safe.
        inlineDynamicImports: true,
      },
    },
  },
  plugins: [
    react(),
    tailwindcss(),
    monkey({
      entry: 'src/main.tsx',
      userscript: {
        name: 'Bakabase 集成脚本',
        namespace: 'http://tampermonkey.net/',
        version: '2.0.2',
        description: 'Bakabase 集成脚本',
        author: 'Bakabase',
        match: isDev ? ['*://*/*'] : prodMatch,
        updateURL: cdnScriptUrl,
        downloadURL: cdnScriptUrl,
        grant: [
          'GM_xmlhttpRequest',
          'GM_getValue',
          'GM_setValue',
          'GM_addStyle',
        ],
        connect: ['localhost', '127.0.0.1', '*'],
        'run-at': 'document-end',
      },
      build: {
        fileName: 'bakabase.user.js',
        externalGlobals: {},
      },
    }),
  ],
});
