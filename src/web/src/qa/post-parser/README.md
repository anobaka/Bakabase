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

Post #10 is a historical pending input whose stored data omits the parsing targets. Reading it
restores the default download-info target, so both its “获取并解析” action and the toolbar's
“开始解析” action create an execution and finish with download/file-processing instructions.
Repeated requests reuse the active execution. Historical records that already have their requested
results remain complete and are excluded from batch starts even when they have no workflow ID.

The suspected-expiry examples also exercise the opt-in unlock action:

- Post #3 has two locked regions sharing one purchase URL. Both unlock for a total of 3 coins,
  with no excluded-item tooltip because every quoted item meets the limits.
- Post #9 has a 3-coin eligible item and an 8-coin excluded item. The button quotes 3 coins;
  hovering shows the excluded 8 coins. Buying keeps the post waiting after partial AI extraction,
  because the second item is still locked.
- Unlocking shows a queued state, a purchasing state, then AI extraction over three seconds.
  Repeated clicks do not charge again. The price cap and minimum account balance are checked
  before spending, and duplicate purchase URLs count once. All balances are offline fixtures.
- Re-parsing a restored or unreported-expiry post automatically buys eligible items. A suspected
  expired post waits for the explicit unlock action, or for purchase at the source followed by
  re-parsing. Change the configuration's purchase limits to inspect the recalculated quotes.

- Clicking a source-post link simulates unlocking its content outside the app. Refreshing/retrying
  that task with “重新解析” then shows a short running state before the full result appears.
- A failed task can be retried. New links or pasted text can be added and parsed.
- Row actions start only that post; repeated requests while it is queued/running are ignored.
  The toolbar starts only posts that have no run, no error and unfinished results, so repeated batch clicks cannot
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
