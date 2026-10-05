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
“开始全部解析任务” action create an execution and finish with download/file-processing instructions.
Repeated requests reuse the active execution. Historical records that already have their requested
results remain complete and are excluded from batch starts even when they have no workflow ID.

The suspected-expiry examples also exercise the opt-in unlock action:

- Post #3 has two locked regions sharing one purchase URL. Both unlock for a total of 3 coins,
  with no excluded-item tooltip because every quoted item meets the limits.
- Post #9 has a 3-coin eligible item and an 8-coin excluded item. The button quotes 3 coins;
  hovering shows the excluded 8 coins. Buying keeps the post waiting after partial AI extraction,
  because the second item is still locked.
- Unlocking is admitted to the shared task queue, then shows purchasing and AI extraction stages.
  Repeated clicks do not charge again. The price cap and minimum account balance are checked
  before spending, and duplicate purchase URLs count once. All balances are offline fixtures.
- Re-parsing a restored or unreported-expiry post automatically buys eligible items. A suspected
  expired post waits for the explicit unlock action, or for purchase at the source followed by
  re-parsing. Change the configuration's purchase limits to inspect the recalculated quotes.

- Clicking a source-post link simulates unlocking its content outside the app. Refreshing/retrying
  that task with “重新解析” then shows a short running state before the full result appears.
- A failed task can be retried. New links or pasted text can be added and parsed.
- Row actions start only that post; repeated requests while it is queued/running are ignored.
  The toolbar starts pending inputs and retries failed, interrupted or cancelled executions; it also
  starts legacy errors without an execution and recovers missing executions. In the initial examples,
  posts #1, #7 and #10 run. Completed posts, running posts and posts awaiting purchase or AI setup
  remain unchanged. Repeated batch and row clicks cannot create duplicate runs. Opening the
  configuration and enabling automatic parsing affects only
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


Choose **并发队列 · 14 个帖子** to load an isolated set of 14 pending inputs. None is queued
until explicitly started. “开始全部解析任务” submits all 14 exactly once: up to 10 tasks enter the
workflow, the remaining four show a clock and “排队中”. The simulated site has one HTTP slot;
AI assessment and extraction share one AI slot. Other admitted posts continue fetching or
purchasing while one post is extracting instructions. The labels follow the real page's BTask
feed, including “获取信息中”, “等待 AI 解析”, “检查失效反馈中”, “购买解锁中”,
“解析下载指令中” and “检查下载链接中”. Defaults can be changed in the actual configuration dialog.
Reducing a limit lets admitted work finish before admitting more; increasing it releases capacity.
Saving configuration never starts untouched inputs.

**模拟重启** discards the in-memory queue and live BTasks, marking previously requested tasks as
interrupted/“待继续”. Inputs that were never started remain “待解析”; neither restarts automatically.
Use a row retry or “开始全部解析任务” to continue. “重置示例” restores the selected scenario and clears all
runtime queues/timers. Switching back to the normal scenarios restores the original ten examples.

The task toolbar includes a global status overview that does not change when searching. Running
work takes precedence over saved results; hover the running count for a breakdown by stage.
The locate icon selects the first actively running task in list order, clears a search that hides
it, scrolls the virtual list to its stable task ID, and highlights it for three seconds. Queued
and paused tasks do not enable the icon. Click again to locate again; incoming updates never
take over scrolling. In the concurrency scenario, scroll away or search for a queued post while
the queue runs, then use the icon to return to the first running task.

Post #5 stores five resource entries for four distinct downloads. Its first Baidu share appears
twice: one entry supplies the access code and link check, and the other includes `?pwd=am26`,
the archive password and processing instructions. The result, resource selectors and exports
should show it once with all of that information retained; the other three resources remain
separate. The 14-post concurrency scenario is unchanged.
