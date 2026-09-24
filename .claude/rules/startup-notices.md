# Startup Notices and the Startup Order

Notices ship with the app: short notes about a release ("the thin client is discontinued")
that the UI opens by itself at startup until they are read. Every dialog the app opens by
itself takes the screen in one fixed order, one at a time.

## The startup order

`src/web/src/components/Startup/startupQueue.ts` is the one place the order is decided:

1. `gettingStarted` — the help center's welcome, the first time a browser opens the dashboard.
2. `notices` — unread notices (`components/Notices/NoticesGate`).
3. `whatsNew` — the release notes of the update just installed (`Changelog/WhatsNewGate`).
4. `pageGuide` — a page's own first-visit guide (`useFirstRunHelp`, path mark pages).

A surface reports `deciding` / `ready` / `idle` through `useStartupSurface`; it shows only on
its turn, keeps the turn until it reports `idle`, and later surfaces wait while an earlier one
is still `deciding`. Turns are handed out once per render's worth of reports (a microtask),
so effect order within a render never decides the order. A surface that is not mounted takes
no part. A new dialog the app opens by itself joins this list — never opens on its own.

Going to a page from anywhere in the notices calls `deferRest()`: the other notices and the
release notes wait for the next launch instead of opening over the page the user asked for.
That is a notice's own route action, and every link in the guide a notice opened — in the
topic it opened or any other the reader moved to, the help center's own Notices list
included: the gate passes `HelpCenterModal` an `onNavigate`, which takes over from its
default (open the page, then `onClose`, which cannot tell "went somewhere" from "closed").
A new way for the flow to leave for a page goes through the gate's `leaveFor` too.
`WhatsNewGate` records the version as seen only once the notes are on screen, so notes put
off this way are not lost.

The dashboard's welcome does the same: its `HelpCenterModal` gets an `onNavigate` that calls
the `deferRest` `useFirstRunHelp` returns, completes the welcome and routes to the page, so a
link out of it leaves the notices and release notes for the next launch. It matters on a
fresh install too — one whose first start imported thin-client pairings has the thin-client
notice waiting behind the welcome (see below). Closing the welcome without going anywhere
hands on to them as usual. Any other first-run guide whose links lead to a page, and that
has surfaces after it, does the same (`pageGuide` is last, so a page's own guide need not).
`pages/dashboard/__tests__/DashboardWelcome.test.tsx` drives the real help center.

The notices take part **once per page load** (`startupDone` in the notice store, so a gate
mounted again — a page outside `BasicLayout` and back — does not start over). Once the gate
has reported `idle` — done, dismissed, put off, nothing to show, the state unreadable, or
nobody to show them to — nothing brings the startup dialog back before the next launch or
reload: not the help center's Notices topic retrying a load that failed, not learning later
who is looking. What those learn shows in the help center only.

## Notices

| Concern | Where |
|---|---|
| Registry (id, version, order, texts, action, audience, upgrade-only, fresh-install exception) | `components/Notices/registry.ts` |
| Texts | `locales/{en,cn}/components/notices.json` |
| Who may see / what is pending | `components/Notices/eligibility.ts` |
| State, load, mark read | `components/Notices/noticeStore.ts` |
| Startup dialog | `NoticesGate.tsx`, `NoticesDialog.tsx` |
| Every notice again | the help center's "Notices" topic (`NoticesTopic.tsx`) |
| Server state | `UIOptions.Notices` (`ReadIds`, `BaselinePending`), `POST /options/ui/notices/{read,baseline}` |

Adding one: a new id (a slug, never renamed or reused — a renamed notice is shown again to
everyone), an `order` above the rest, texts in both languages, an audience, and whether it is
upgrade-only. `registry.test.ts` checks keys and placeholders.

### Read state is per install

Kept in the server's UI options, not in browser storage, so every window and browser of the
install agrees; the server pushes the options to every window when they change, so a notice
read in one window leaves the others at once. Marking is idempotent and keeps ids the running
build does not know (a newer build may have written them). Closing the dialog without reading
keeps the rest for the next launch — `sessionStorage`, so a reload does not bring them back.

### Audience

- `local` (default) — someone at the machine running the install: the desktop app's window.
- `lanAdmin` — a browser on another device that may change this install (`Unrestricted`, the
  container default). The only way anyone sees a headless server's own UI, so a notice about
  the server itself belongs here; notices about the desktop app do not.
- Never: a console relay window or the retired thin client (`ClientMode.PureClient`). That
  page is another server's UI talking to its API; a notice read there would be recorded on
  that server. Nor a LAN browser that may only read. The gate makes no request at all there.
- Nobody, either, when `/remote-access/context` could not be read — the request failed or
  came back without a context (`context: "unknown"` in the remote-access store). The store
  keeps its defaults — local, all-in-one — so the rest of the desktop app works, but those
  defaults would take a LAN browser for this install's window. Missing the notices once is
  safe; showing and recording them for the wrong viewer is not. Read `context`, never
  `initialized`, for who is looking.

### Fresh installs and upgrade-only notices

An upgrade-only notice describes a change from a version a fresh install never ran. The
server cannot know the UI's registry, and the frontend compares no versions, so:

1. `NoticeBaselineInitializer` sets `BaselinePending` on an install's first start. It reads
   `AppOptions.Version` — the version the install last ran, until the host records the
   running one — in its **constructor**: the host constructs every hosted service before it
   starts any, so the answer holds wherever a running host records the version (today a task
   started from `ApplicationStarted`; a hosted service before or after it would do as well).
   Only recording it before the host is built could break this, and the data migrations,
   which read the same value while the host runs, would break with it.
   `NoticeBaselineStartupOrderTests` runs a real host with each of those, and the browser
   smoke's first-launch stage (`federation-browser-smoke/first-launch.cjs`) checks the
   baseline a fresh desktop fixture opened through the app's own start-up (`AppHost`).
2. The first UI that loads the state records every upgrade-only notice it ships with as read
   (`CaptureNoticeBaseline`), which closes the baseline for good.
3. Notices added by later releases are not in that baseline, so the install sees them after
   it updates. While the baseline is open — or failed to save — upgrade-only notices stay
   hidden. An install that predates notices never opened a baseline and sees them all.

An upgrade-only notice can name a `showOnFreshInstallWhen` fact: a fresh install where it
holds leaves the notice out of its baseline and so is shown it like an upgraded install.
The facts are asked once, just before the baseline is recorded (`learnFreshInstallFacts`),
and only where they can be answered; an unknown answer keeps the notice upgrade-only.

- `thinClientPairingsImported` (the thin-client notice) — a managed server in
  `/federation/local/servers` has `importedFromLegacyClient`: the desktop app's first start
  brought over an old thin client's pairings, so whoever installed it used the thin client.
  Asked only by this install's own window (the route is local-only; a LAN browser keeps the
  notice upgrade-only). The import starts with the host, well before the window opens after
  migrations; an import run later from the devices page comes after the baseline and changes
  nothing. The smoke's first-launch stage opens that fixture's window and expects this notice
  alone.

`introducedIn` is display only; nothing compares it.
