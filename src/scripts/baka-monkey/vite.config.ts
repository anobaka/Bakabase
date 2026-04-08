import react from '@vitejs/plugin-react-swc';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import monkey from 'vite-plugin-monkey';

const isDev = process.env.NODE_ENV !== 'production';

const cdnScriptUrl = 'https://cdn-public.anobaka.com/app/bakabase/inside-world/scripts/bakabase.user.js';

const prodMatch = [
  'https://exhentai.org/*',
  'https://www.north-plus.net/*',
];

export default defineConfig({
  define: {
    __DEV__: JSON.stringify(isDev),
  },
  plugins: [
    react(),
    tailwindcss(),
    monkey({
      entry: 'src/main.tsx',
      userscript: {
        name: 'Bakabase 集成脚本',
        namespace: 'http://tampermonkey.net/',
        version: '2.0.0',
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
