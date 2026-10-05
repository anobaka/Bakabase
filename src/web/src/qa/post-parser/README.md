# Post parser interaction preview

From `src/web`, run:

```sh
node node_modules/vite/bin/vite.js --config vite.qa-post-parser.config.ts
```

Open <http://127.0.0.1:4317/qa-post-parser.html>.

This entry imports the actual post-parser page, context provider, styles, translations and dialogs.
The separate development config aliases only the API client and SignalR connection to local mocks.
It rejects production builds and is not referenced by the production HTML, router or main entry.
The entry also checks a dedicated config flag before importing application components; opening
this HTML on the normal development server shows only the safe startup instructions.
No backend, logged-in account, network share, file download or disk mutation is used.

Examples cover pending input, awaiting source-site unlock, suspected expiry, partial results,
a complete eight-step plan with three extraction layers, rename and move operations,
four independent resources, all four link-health display states, retryable fetch failure and
missing AI configuration. The toolbar can isolate states or reset all examples.

- Clicking a source-post link simulates unlocking its content outside the app. Refreshing/retrying
  that task then shows a short running state before the full result appears.
- A failed task can be retried. New links or pasted text can be added and parsed.
- Row actions start only that post; repeated requests while it is queued/running are ignored.
  The toolbar starts only posts that have no run and no error, so repeated batch clicks cannot
  create duplicate runs. Opening the configuration and enabling automatic parsing affects only
  subsequent additions (including a pending link explicitly submitted again), not older pending posts.
- Row history filters by the post's ID and includes its earlier revisions. The toolbar history shows
  all post-parser runs, including a failed earlier run for the autumn-scene example under an older
  workflow definition. Re-parsing appends history; deleting a post does not erase its run records.
- Acquisition imports and local processing create mock IDs only. A sample local directory is
  `/Users/demo/Downloads/秋日场景素材包`; entering it never reads or changes real files.
- Copy and JSON/spreadsheet exports use the actual frontend behavior and can write to the
  clipboard or save an example export when explicitly clicked.
- Secondary API methods return empty offline data. Changes reset on reload.

Keep the Vite process running for review. Normal frontend tests and production builds continue
using the real API implementation without the development-only aliases.

The preview and page form a full-height flex column. The toolbar remains above the list, and the
list uses the remaining height; on a short viewport the outer content can scroll to retain the
minimum useful list height.
