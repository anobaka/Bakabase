import { describe, expect, it } from "vitest";

import { remoteDevicePlatformLabelKey } from "@/core/remoteDevicePlatform";
import {
  ManagedServerOutcome,
  ManagedServerOutcomeLabel,
  ManagedServerState,
  RemoteDevicePlatform,
} from "@/sdk/constants";

/*
 * Every key the server-switching screens can show exists in both languages, with the same
 * placeholders.
 *
 * The keys are read out of the components' own source rather than listed by hand, so a
 * string added to a component without a translation fails here instead of rendering as its
 * key. Keys a component builds at runtime cannot be read that way; their families are
 * enumerated below from the same enums the components use.
 */

type Resources = Record<string, string>;

const merge = (modules: Record<string, Resources>): Resources =>
  Object.assign({}, ...Object.values(modules));

// The same files `i18n.ts` merges into one flat namespace per language.
const en = merge(
  import.meta.glob<Resources>("../../../locales/en/**/*.json", { eager: true, import: "default" }),
);
const cn = merge(
  import.meta.glob<Resources>("../../../locales/cn/**/*.json", { eager: true, import: "default" }),
);

const sources = import.meta.glob<string>(
  [
    "../DevicesPage.tsx",
    "../components/common.tsx",
    "../components/ConfirmDialog.tsx",
    "../components/ManagedServers.tsx",
    "../components/ManagementAccess.tsx",
    "../../../layouts/BasicLayout/components/PageNav/components/ServerSwitcher/index.tsx",
    "../../../pages/client-path-mapping/index.tsx",
  ],
  { eager: true, query: "?raw", import: "default" },
);

/** Namespaces these screens draw from; a quoted string under one of them is a key. */
const keyPattern =
  /["'`]((?:federation|clientPathMapping|configuration\.remoteAccess|log)\.[A-Za-z0-9_.]*[A-Za-z0-9_])["'`]/g;

const staticKeys = (source: string) => Array.from(source.matchAll(keyPattern), (match) => match[1]);

const dynamicKeys = [
  // `federation.servers.state.${state}` — the switcher and the servers list.
  ...Object.values(ManagedServerState)
    .filter((value): value is ManagedServerState => typeof value === "number")
    .map((state) => `federation.servers.state.${state}`),
  // `federation.error.ManagedServer${label}` — every outcome but success is shown.
  ...Object.values(ManagedServerOutcome)
    .filter(
      (value): value is ManagedServerOutcome =>
        typeof value === "number" && value !== ManagedServerOutcome.Ok,
    )
    .map((outcome) => `federation.error.ManagedServer${ManagedServerOutcomeLabel[outcome]}`),
  // `federation.management.status.${key}` in ManagementAccess.
  ...["off", "paired", "open", "unrestricted"].map(
    (status) => `federation.management.status.${status}`,
  ),
  // Codes `/federation/local/servers` refuses with, shown through `federation.error.${code}`:
  // ErrorNotice, and the switcher's own description of a failed switch.
  "federation.error.ManagementUnavailable",
  "federation.error.ServerNotManaged",
  "federation.error.InvalidRequest",
  "federation.error.UnsupportedMediaType",
  "federation.error.RelayUnavailable",
  // …and those the console's `/client/switcher/{id}/open` refuses with, which
  // `openConsoleTarget` carries over as the same kind of error.
  "federation.error.UnknownServer",
  // Paired devices and requests name their platform.
  ...Object.values(RemoteDevicePlatform)
    .filter((value): value is RemoteDevicePlatform => typeof value === "number")
    .map(remoteDevicePlatformLabelKey),
  // The console's menu group (routesMenuConfig) and its page.
  "menu.client.thisComputer",
  "menu.client.pathMapping",
];

const placeholders = (text: string) =>
  Array.from(text.matchAll(/{{\s*([\w.]+)\s*}}/g), (match) => match[1]).sort();

describe("locales for the server-switching screens", () => {
  const keys = Array.from(
    new Set([...Object.values(sources).flatMap(staticKeys), ...dynamicKeys]),
  ).sort();

  it("reads keys out of every listed component", () => {
    expect(Object.keys(sources)).toHaveLength(7);
    for (const [path, source] of Object.entries(sources)) {
      expect(staticKeys(source).length, path).toBeGreaterThan(0);
    }
    // Spot checks that the reading finds what the screens actually say, keys picked in
    // ternaries and helper arguments included.
    expect(keys).toEqual(
      expect.arrayContaining([
        "federation.switcher.listFailed",
        "federation.switcher.managingName",
        "federation.management.requirePairingConfirmUnrestricted",
        "federation.management.devices.revokeSelfWarning",
        "federation.servers.import.nothingNew",
        "federation.servers.retrying",
        "federation.servers.add.discovering",
        "federation.servers.add.alreadyManaged",
        "federation.console.switchToThisDevice",
        "clientPathMapping.onlyInClient",
        "menu.client.thisComputer",
      ]),
    );
  });

  it.each(keys)("%s exists in English and Chinese with the same placeholders", (key) => {
    expect(en[key], `en: ${key}`).toEqual(expect.any(String));
    expect(cn[key], `cn: ${key}`).toEqual(expect.any(String));
    expect(en[key].trim(), `en: ${key}`).not.toBe("");
    expect(cn[key].trim(), `cn: ${key}`).not.toBe("");
    expect(placeholders(cn[key]), key).toEqual(placeholders(en[key]));
  });

  it.each([
    ["English", en],
    ["Chinese", cn],
  ])(
    "names the managed-server fields apart from library sharing's in %s",
    (_, locale: Resources) => {
      // The desktop app's devices page has both forms, one above the other. A field named
      // like another is the same field to a screen reader and to a label-based lookup.
      expect(locale["federation.servers.add.address"]).not.toEqual(
        locale["federation.pair.address"],
      );
      expect(locale["federation.servers.add.code"]).not.toEqual(locale["federation.pair.code"]);
    },
  );

  it("walks an unrestricted server's owner through the settings the page names", () => {
    // Every direction the card hands over is said: the page, the setting, its value and
    // the switch that follows — in the words those settings are shown with.
    expect(placeholders(en["federation.servers.unrestricted"])).toEqual([
      "enabled",
      "name",
      "page",
      "pairing",
      "setting",
    ]);
  });
});
