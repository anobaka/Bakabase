# Multi-device browser smoke tests

This suite runs the production frontend against independent temporary Service hosts.
The unified host composes the same server and management relay as the desktop app;
the source host is headless. It never opens an installed application or launches a
native player. The separate fresh-install fixture checks first-launch behavior.

```bash
cd src/tests/federation-browser-smoke
npm ci --ignore-scripts
npx playwright install --with-deps --only-shell chromium
cd ../../..
python3 src/tests/federation-browser-smoke/run.py \
  --dotnet /absolute/path/to/dotnet --node /absolute/path/to/node \
  --results-directory /absolute/path/to/results
```

Build `src/web` first, or use `--web-root` for an existing production build. The runner
builds the test host unless `--no-build` is supplied. CI downloads the exact shared
host and frontend artifacts and retains its existing required-check gates.

The stages run in this order:

1. **First launch:** a new desktop data directory manages no other devices, opens its
   startup-notice baseline, records shipped notices and shows no upgrade-only notice.
2. **Management and server switching:** A pairs with B through the Devices page,
   switches to B through the signed relay and returns home. HTTP and WebSocket checks
   validate the correct origin, signer, replay protection and rejection of cross-origin
   host-only requests. Path mappings stay on A. Device-map layout and keyboard focus
   are checked at desktop widths and with a crowded map. The stage leaves A managing B.
3. **Resource profiles:** A creates a local profile for B through the existing filter
   and player editors. Colliding property IDs resolve against B, preview matches only
   B's resources, and local player arguments stay on A. No executable is launched;
   temporary profiles and properties are removed before data-sync tests.
4. **Management path mappings:** a closed editor sends no tree request. Opening it
   reads a bounded owned-path tree, selecting a directory uses its actual server path,
   and editing does not save automatically. A draft survives reload, restores its
   values and is cleared on a successful save. A draft whose baseline changed cannot
   overwrite another saved edit without review; choosing the current mapping keeps it.
   The original management pairing and mappings are restored after the stage.
5. **Data sync:** a two-way request starts on the device map and is approved through
   B's signed relay. First-sync review, definitions convergence, conflicts, resolution,
   hub updates, keyboard focus and narrow layouts are exercised. A LAN browser's
   restricted sharing controls are checked independently of administrator pairing.

The retired read-only resource page, grants, playback sessions and mapping editor
are not exercised or retained. Unified resources, management, and data sync are the
supported surfaces.

`network.cjs` confines Chromium to the fixture listeners and the management relay
range, blocks external HTTP/WebSocket traffic, and proves the containment before any
page is opened. Fixtures explicitly disable all analytics. Timeouts stop only owned
process groups; temporary data is removed unless `--keep-fixtures` is supplied. Failure
artifacts contain screenshots and allowlisted diagnostics without pairing credentials.

Offline runner tests:

```bash
python3 -m unittest discover -s src/tests/federation-browser-smoke -p 'test*.py'
node --test src/tests/federation-browser-smoke/first-launch.test.cjs
```

The native tray switcher, Windows installer and an actual external player window are
outside this browser fixture and remain desktop acceptance checks.
