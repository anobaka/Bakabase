import type { FederationStatus, ManagedServersView } from "../types";
import type * as Switching from "../switching";
import type { BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings } from "@/sdk/Api";
import type { MockInstance } from "vitest";
import type * as DataSyncApi from "@/features/data-sync/api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DeviceMapPage, { ON_DEMAND_QUERY } from "../DeviceMapPage";
import DeviceMapCanvas, { ATTENTION_DASH } from "../map/DeviceMapCanvas";
import { buildDeviceGraph } from "../map/graph";
import { federationPeerApi } from "../peerApi";
import { managedServerApi } from "../serverApi";
import { openManagedServer } from "../switching";

import {
  access,
  inTenMinutes,
  manager,
  managementRequestIn,
  managementRequestOut,
  server,
  servers,
  status,
} from "./deviceMapFixtures";

import BApi from "@/sdk/BApi";
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
import {
  ClientMode,
  DataSyncLinkState,
  ManagedServerOutcome,
  ManagedServerState,
  RemoteAccessMode,
  RemoteDevicePlatform,
  ServerKind,
} from "@/sdk/constants";
import { dataSyncApi } from "@/features/data-sync/api";
import { useDataSyncStore } from "@/features/data-sync/stores/dataSync";
import {
  mapPeer,
  mapRequest,
  mapView,
  overview as syncOverview,
  ownRequest,
} from "@/features/data-sync/__tests__/dataSyncFixtures";

/** Every key asked for while rendering, with the values it was asked with. */
const used = vi.hoisted(() => new Map<string, Record<string, unknown> | undefined>());

vi.mock("react-i18next", () => ({
  // Keys as text, followed by the interpolated values, so a test can see what was said.
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) => {
      used.set(key, options);

      return options
        ? [key, ...Object.values(options).filter((value) => value !== undefined)].join(" ")
        : key;
    },
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({
      initialized: true,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
      serverName: "studio-server",
    }),
  useIsPureClient: () => false,
}));
vi.mock("../peerApi", () => ({
  federationPeerApi: { status: vi.fn(), setName: vi.fn(), resetIdentity: vi.fn() },
}));
vi.mock("../serverApi", () => ({
  managedServerApi: {
    list: vi.fn(),
    discover: vi.fn(),
    pair: vi.fn(),
    probe: vi.fn(),
    forget: vi.fn(),
    cancelRequest: vi.fn(),
    setPathMappings: vi.fn(),
    open: vi.fn(),
  },
}));
vi.mock("@/core/clientApi", () => ({
  LOCAL_SWITCHER_TARGET: "local",
  clientApi: { switcher: { list: vi.fn(), open: vi.fn() } },
}));
vi.mock("../switching", async (importOriginal) => ({
  ...(await importOriginal<typeof Switching>()),
  openManagedServer: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    management: { getManagedDeviceName: vi.fn(), setManagedDeviceName: vi.fn() },
    options: {},
    remoteAccess: {
      getRemoteAccessSettings: vi.fn(),
      revokeRemoteAccessDevice: vi.fn(),
      approveRemoteDevicePairingRequest: vi.fn(),
      rejectRemoteDevicePairingRequest: vi.fn(),
    },
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));
vi.mock("@/features/data-sync/api", async (importOriginal) => ({
  ...(await importOriginal<typeof DataSyncApi>()),
  dataSyncApi: {
    map: vi.fn(),
    overview: vi.fn(),
    peers: vi.fn().mockResolvedValue([]),
    createLink: vi.fn(async () => ({})),
    updateLink: vi.fn(async () => ({})),
    resetLink: vi.fn(async () => undefined),
    approveRequest: vi.fn(async () => ({ readBackGranted: false })),
    rejectRequest: vi.fn(async () => undefined),
    setSharing: vi.fn(async () => undefined),
    revokeReader: vi.fn(async () => undefined),
    syncNow: vi.fn(async () => ({})),
    pauseLink: vi.fn(async () => ({})),
  },
}));

const ok = { code: 0 };

/** Current device identity, management in both directions and definitions synchronization. */
let sharing: FederationStatus;
let managed: ManagedServersView;
let settings: RemoteAccessSettings;
let syncView: DataSyncApi.DataSyncMapView;

/**
 * A home office: a NAS shared both ways and managed from here; a laptop that browses this
 * library, and a device of the laptop's name that manages this one — which a name alone never
 * makes the laptop.
 */
const homeOffice = () => {
  sharing = status();
  managed = servers({
    // The same install as the peer: its sharing NodeId is its ServerId.
    servers: [
      server("nas", { name: "NAS", address: "http://192.168.1.20:34567" }),
      server("lap", { name: "Laptop", address: "http://192.168.1.31:40211" }),
    ],
  });
  settings = access({
    devices: [manager("d-lap", "Laptop")],
    pendingRequests: [managementRequestIn("m1", "Guest", { platform: RemoteDevicePlatform.Linux })],
  });
  // Nothing synced yet.
  syncView = mapView();
};

function Where() {
  const location = useLocation();

  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={["/federation/map"]}>
      <Routes>
        <Route element={<DeviceMapPage />} path="/federation/map" />
        <Route element={<Where />} path="*" />
      </Routes>
    </MemoryRouter>,
  );

const node = (id: string) =>
  document.querySelector<SVGGElement>(`[data-node="${id}"][role="button"]`)!;
const edge = (id: string) =>
  document.querySelector<SVGGElement>(`[data-edge="${id}"][role="button"]`)!;
/** A device, or a relationship, where the map lists them instead of drawing them. */
const listed = (id: string) =>
  document.querySelector<HTMLButtonElement>(`button[data-node="${id}"]`)!;

/**
 * What Chromium does and jsdom does not: a focused control that is disabled loses focus, to
 * the page's body — before the page does anything else. (jsdom will not blur what cannot take
 * focus, so the control is briefly enabled to let go of it.)
 */
const blurWhenDisabled = () => {
  const observer = new MutationObserver((records) => {
    for (const record of records) {
      const target = record.target as HTMLElement;

      if (target === document.activeElement && target.matches(":disabled")) {
        target.removeAttribute("disabled");
        target.blur();
        target.setAttribute("disabled", "");
      }
    }
  });

  observer.observe(document.body, {
    attributes: true,
    attributeFilter: ["disabled"],
    subtree: true,
  });

  return () => observer.disconnect();
};

/** A window narrower than the widest: the details are shown on demand, beside the map. */
let wideWindow: (() => void) | undefined;
const narrowWindow = () => {
  const original = window.matchMedia;

  window.matchMedia = ((query: string) => ({
    ...original(query),
    matches: query === ON_DEMAND_QUERY,
    media: query,
  })) as typeof window.matchMedia;
  wideWindow = () => {
    window.matchMedia = original;
  };
};
const panel = () => screen.getByTestId("device-map-panel");
const loaded = () => waitFor(() => expect(node("server:nas")).not.toBeNull());
const dialog = () => screen.getByRole("alertdialog");

beforeEach(() => {
  vi.clearAllMocks();
  used.clear();
  homeOffice();
  vi.mocked(federationPeerApi.status).mockImplementation(async () => sharing);
  vi.mocked(managedServerApi.list).mockImplementation(async () => managed);
  vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockImplementation(
    async () => ({ ...ok, data: settings }) as never,
  );
  vi.mocked(BApi.remoteAccess.revokeRemoteAccessDevice).mockResolvedValue(ok as never);
  vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockResolvedValue(ok as never);
  vi.mocked(BApi.remoteAccess.rejectRemoteDevicePairingRequest).mockResolvedValue(ok as never);
  vi.mocked(dataSyncApi.peers).mockResolvedValue([]);
  vi.mocked(managedServerApi.discover).mockResolvedValue({ servers: [] });
  vi.mocked(managedServerApi.probe).mockResolvedValue({
    outcome: ManagedServerOutcome.Unreachable,
    pairingSupported: false,
    alreadyManaged: false,
  });
  useDataSyncStore.getState().clear();
  vi.mocked(dataSyncApi.map).mockImplementation(async () => syncView);
  vi.mocked(dataSyncApi.overview).mockResolvedValue(syncOverview({ deviceName: "Studio PC" }));
});
afterEach(() => {
  cleanup();
  wideWindow?.();
  wideWindow = undefined;
});

describe("device map: the picture", () => {
  it("shows what each device says it is: a headless server, a desktop app, and what it runs on", async () => {
    managed = servers({
      servers: [
        server("nas", {
          name: "NAS",
          address: "http://192.168.1.20:34567",
          kind: ServerKind.Headless,
          platform: RemoteDevicePlatform.Linux,
        }),
      ],
    });
    renderPage();
    await loaded();
    await waitFor(() => expect(node("server:nas")).toHaveAttribute("data-kind", "server"));

    expect(node("server:nas").getAttribute("aria-label")).toContain("federation.map.kind.server");
    // A device manages this one: a pairing only a desktop app or a phone makes.
    expect(node("manager:d-lap")).toHaveAttribute("data-kind", "desktop");
  });

  it("names every device and relationship for assistive technology, and lists them in words", async () => {
    renderPage();
    await loaded();

    expect(screen.getByRole("group", { name: "federation.map.canvasLabel" })).toBeInTheDocument();
    expect(node("server:nas").getAttribute("aria-label")).toContain("NAS");
    expect(edge("management:manager:d-lap").getAttribute("aria-label")).toContain(
      "federation.map.direction.management.in.active Laptop",
    );
    const list = screen.getByTestId("device-map-list");

    expect(list).toHaveClass("sr-only");
    expect(
      within(list).getByText(/federation\.map\.direction\.management\.in\.pending Guest/),
    ).toBeInTheDocument();
    expect(
      within(list).getByText(/federation\.map\.direction\.management\.out\.active NAS/),
    ).toBeInTheDocument();
  });

  it("explains the lines, and says nothing about kinds the product does not have yet", async () => {
    renderPage();
    await loaded();
    const legend = screen.getByTestId("device-map-legend");

    expect(legend.querySelector('[data-legend="sharing"]')).toBeNull();
    expect(legend.querySelector('[data-legend="management"]')).not.toBeNull();
    expect(legend.querySelector('[data-legend="pending"]')).not.toBeNull();
    expect(legend.querySelector('[data-legend="sync"]')).toBeNull();
    expect(used.has("federation.map.legend.sync")).toBe(false);
  });
});

describe("device map: relationships that do not work right now", () => {
  it("marks them by style and shape as well as colour, names why, and explains the mark", async () => {
    managed = servers({
      servers: [
        server("den", { name: "Den server", state: ManagedServerState.Revoked }),
        server("old", {
          name: "Old NAS",
          state: ManagedServerState.WrongServer,
          answeredBy: { serverId: "other", name: "Other", isThisDevice: false },
        }),
        server("nas", { name: "NAS", address: "http://192.168.1.20:34567" }),
      ],
    });
    renderPage();
    await loaded();
    const old = edge("management:server:old");
    const lane = old.querySelector("line[data-direction]")!;

    // Its own line style (dots, not the dashes of a request) and a warning shape on its badge.
    expect(lane).toHaveAttribute("data-attention", "true");
    expect(lane).toHaveAttribute("stroke-dasharray", ATTENTION_DASH);
    expect(old.querySelector("[data-attention-mark]")).not.toBeNull();
    // What does not work, and why, in its name and in the list read out.
    expect(old.getAttribute("aria-label")).toContain(
      "federation.map.attention.management.out Old NAS federation.map.issue.wrongServer",
    );
    expect(edge("management:server:den").getAttribute("aria-label")).toContain(
      "federation.map.attention.management.out Den server federation.map.issue.revoked",
    );
    expect(
      within(screen.getByTestId("device-map-list")).getByText(
        /federation\.map\.attention\.management\.out Old NAS federation\.map\.issue\.wrongServer/,
      ),
    ).toBeInTheDocument();
    // A working one carries none of it.
    const working = edge("management:server:nas");

    expect(working.querySelector("[data-attention]")).toBeNull();
    expect(working.querySelector("[data-attention-mark]")).toBeNull();
    // And the legend says what the mark means.
    const legend = screen
      .getByTestId("device-map-legend")
      .querySelector('[data-legend="attention"]');

    expect(legend).toHaveTextContent("federation.map.legend.attention");
    expect(legend?.querySelector("[data-attention-mark]")).not.toBeNull();
  });
});

describe("device map: names", () => {
  const names = [
    "DESKTOP-7F3K2QH",
    "DESKTOP-7F3K2QX",
    "DESKTOP-9QW4E2A",
    "LAPTOP-3JK2L1M8",
    "Mac-mini-de-Jax",
    "Living-room-media-server-A",
    "Living-room-media-server-B",
  ];
  const graph = buildDeviceGraph({
    servers: servers({
      servers: names.map((label, i) =>
        server(`p${i}`, {
          name: label,
          address: `http://192.168.1.${i + 10}:34567`,
        }),
      ),
    }),
  });

  it.each([900, 1200])("never shortens two names alike, and says each in full @%i", (width) => {
    render(
      <MemoryRouter>
        <DeviceMapCanvas graph={graph} initialWidth={width} onSelect={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByTestId("device-map-canvas")).toHaveAttribute("data-mode", "map");
    const shown = names.map((_, i) => node(`server:p${i}`).querySelector("text")!.textContent!);

    // Every card reads differently, and keeps the end that tells it apart…
    expect(new Set(shown).size).toBe(names.length);
    shown.forEach((label, i) => expect(label.endsWith(names[i].slice(-2)), label).toBe(true));
    // …and where there is room, default machine names are whole.
    if (width >= 1200) expect(shown.slice(0, 5)).toEqual(names.slice(0, 5));
    names.forEach((name, i) => {
      // The full name is its tooltip and its accessible name.
      expect(node(`server:p${i}`).querySelector("title")).toHaveTextContent(name);
      expect(node(`server:p${i}`).getAttribute("aria-label")).toContain(name);
    });
  });

  it("lists them, every name whole, where the width cannot draw them readably", () => {
    render(
      <MemoryRouter>
        <DeviceMapCanvas graph={graph} initialWidth={600} onSelect={vi.fn()} />
      </MemoryRouter>,
    );

    expect(screen.getByTestId("device-map-canvas")).toHaveAttribute("data-mode", "list");
    names.forEach((name, i) =>
      expect(listed(`server:p${i}`)).toHaveTextContent(new RegExp(`^${name}`)),
    );
  });
});

describe("device map: keyboard", () => {
  it("opens the details with Enter, reads them out, and gives focus back on Escape", async () => {
    renderPage();
    await loaded();
    const nas = node("server:nas");

    act(() => nas.focus());
    fireEvent.keyDown(nas, { key: "Enter" });
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:nas"));
    const heading = within(panel()).getByRole("heading", { level: 2 });

    expect(heading).toHaveTextContent("NAS");
    expect(heading).toHaveFocus();
    expect(nas).toHaveAttribute("aria-pressed", "true");

    fireEvent.keyDown(heading, { key: "Escape" });
    await waitFor(() => expect(panel()).toHaveAttribute("data-overview", "true"));
    expect(nas).toHaveFocus();
  });
});

describe("device map: acting on a device", () => {
  const open = async (id: string) => {
    renderPage();
    await loaded();
    fireEvent.click(node(id));
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", `node:${id}`));
  };

  it("asks before stopping to manage a server, then forgets it by its own identity", async () => {
    vi.mocked(managedServerApi.forget).mockResolvedValue({ changed: true });
    await open("server:nas");
    fireEvent.click(
      within(screen.getByTestId("management-out")).getByRole("button", {
        name: "federation.servers.forget",
      }),
    );
    expect(dialog()).toHaveTextContent("federation.servers.forgetConfirm NAS");
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    expect(managedServerApi.forget).toHaveBeenCalledWith("nas");
  });

  it("switches the window to a managed server without asking", async () => {
    await open("server:nas");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "federation.servers.open" }));
    });

    expect(openManagedServer).toHaveBeenCalledWith("nas");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("asks before revoking a device that manages this one, through remote access", async () => {
    await open("manager:d-lap");
    fireEvent.click(
      within(screen.getByTestId("management-in")).getByRole("button", {
        name: "federation.management.devices.revoke",
      }),
    );
    expect(dialog()).toHaveTextContent("federation.management.devices.revokeConfirm Laptop");
    expect(BApi.remoteAccess.revokeRemoteAccessDevice).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    expect(BApi.remoteAccess.revokeRemoteAccessDevice).toHaveBeenCalledWith("d-lap", {
      showErrorToast: false,
    });
  });

  it("says on both devices of one name that they may be one, and never draws them as one", async () => {
    await open("server:lap");
    const namesakes = screen.getByTestId("device-map-namesakes");

    expect(namesakes).toHaveTextContent("federation.map.panel.namesakes.one");
    expect(screen.queryByText(/federation\.map\.panel\.matchedByName/)).toBeNull();
    // The laptop's details say nothing it does not have: no line from what manages this one.
    expect(screen.queryByTestId("management-in")).toBeNull();
    // The other is named by what it is to this device, and what it runs on.
    const manages = within(namesakes).getByRole("button", {
      name: "federation.map.panel.showNamesake Laptop federation.map.namesake.what federation.map.namesake.role.manager configuration.remoteAccess.platform.windows",
    });

    fireEvent.click(manages);
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:manager:d-lap"));
    expect(screen.getByTestId("management-in")).toHaveAttribute("data-status", "active");
    // And the other way: the laptop, which browses this library, by its address.
    const browses = within(screen.getByTestId("device-map-namesakes")).getByRole("button", {
      name: "federation.map.panel.showNamesake Laptop federation.map.namesake.what federation.map.namesake.role.managed http://192.168.1.31:40211",
    });

    fireEvent.click(browses);
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:lap"));
  });

  it("tells three or more devices of one name apart by what each is and where", async () => {
    // Two pairings of the laptop's name manage this device — the same system, paired on
    // different days — and a server of that name answers nearby.
    settings = access({
      ...settings,
      devices: [
        manager("d-lap", "Laptop"),
        manager("d-lap2", "laptop", { createdAt: "2026-09-10T08:30:00Z" }),
      ],
    });
    vi.mocked(managedServerApi.discover).mockResolvedValue({
      servers: [
        {
          serverId: "srv-lap",
          name: "LAPTOP",
          address: "http://10.0.0.7:34567",
          appVersion: "2.4.0",
          alreadyManaged: false,
        },
      ],
    });
    renderPage();
    await loaded();
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "federation.servers.add.discover" }));
    });
    fireEvent.click(node("server:lap"));
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:lap"));
    const namesakes = screen.getByTestId("device-map-namesakes");
    const rows = within(namesakes).getAllByRole("button");
    const names = rows.map((row) => row.textContent ?? "");

    expect(namesakes).toHaveTextContent("federation.map.panel.namesakes.many 3");
    // Every row, and every button's accessible name, says something the others do not.
    expect(new Set(names).size).toBe(3);
    rows.forEach((row) => expect(row).toHaveAccessibleName(row.textContent ?? ""));
    const [first, second, nearby] = ["manager:d-lap", "manager:d-lap2", "ghost:10.0.0.7:34567"].map(
      (id) => within(namesakes.querySelector(`[data-namesake="${id}"]`)!).getByRole("button"),
    );

    // Found nearby, at its address.
    expect(nearby).toHaveTextContent(
      "federation.map.panel.showNamesake LAPTOP federation.map.namesake.what federation.map.namesake.role.nearby http://10.0.0.7:34567",
    );
    // Both manage this device from Windows: when each was paired tells them apart.
    for (const [row, createdAt] of [
      [first, "2026-09-01T00:00:00Z"],
      [second, "2026-09-10T08:30:00Z"],
    ] as const) {
      expect(row).toHaveTextContent("federation.map.namesake.role.manager");
      expect(row).toHaveTextContent("configuration.remoteAccess.platform.windows");
      expect(row).toHaveTextContent(
        `federation.map.namesake.paired ${new Date(createdAt).toLocaleString()}`,
      );
    }
    for (const key of [
      "federation.map.namesake.what",
      "federation.map.namesake.paired",
      "federation.map.namesake.role.nearby",
      "federation.map.namesake.role.manager",
      "federation.map.panel.namesakes.many",
      "federation.map.panel.showNamesake",
    ])
      expect(used.has(key), key).toBe(true);

    fireEvent.click(nearby);
    await waitFor(() =>
      expect(panel()).toHaveAttribute("data-selection", "node:ghost:10.0.0.7:34567"),
    );
    // From there, the laptop by its address, the two pairings by what runs them — no two alike.
    const fromNearby = within(screen.getByTestId("device-map-namesakes")).getAllByRole("button");

    expect(
      fromNearby.map((row) => row.closest("li")!.getAttribute("data-namesake")).sort(),
    ).toEqual(["manager:d-lap", "manager:d-lap2", "server:lap"]);
    expect(new Set(fromNearby.map((row) => row.textContent)).size).toBe(3);
  });

  it("asks before letting a device manage this one, and rejects without asking", async () => {
    await open("manager-request:m1");
    fireEvent.click(screen.getByRole("button", { name: "federation.management.requests.approve" }));
    expect(dialog()).toHaveTextContent("federation.management.requests.approveConfirmFrom Guest");
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });
    expect(BApi.remoteAccess.approveRemoteDevicePairingRequest).toHaveBeenCalledWith("m1", {
      showErrorToast: false,
    });

    await act(async () => {
      fireEvent.click(
        screen.getByRole("button", { name: "federation.management.requests.reject" }),
      );
    });
    expect(BApi.remoteAccess.rejectRemoteDevicePairingRequest).toHaveBeenCalledWith("m1", {
      showErrorToast: false,
    });
  });

  it("surfaces a refusal in the dialog, next to the decision", async () => {
    vi.mocked(BApi.remoteAccess.revokeRemoteAccessDevice).mockResolvedValue({
      code: 1,
      message: "Not now",
    } as never);
    await open("manager:d-lap");
    fireEvent.click(
      within(screen.getByTestId("management-in")).getByRole("button", {
        name: "federation.management.devices.revoke",
      }),
    );
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    expect(within(dialog()).getByRole("alert")).toHaveTextContent("Not now");
  });

  it("renames this device", async () => {
    vi.mocked(BApi.management.getManagedDeviceName).mockResolvedValue({
      name: "Studio PC",
      libraryEpoch: "local-epoch",
      environmentControlled: false,
    });
    vi.mocked(BApi.management.setManagedDeviceName).mockResolvedValue({
      name: "Den",
      libraryEpoch: "local-epoch",
      environmentControlled: false,
    });
    renderPage();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    fireEvent.change(await screen.findByLabelText("federation.name.label"), {
      target: { value: "  Den  " },
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    });

    expect(BApi.management.setManagedDeviceName).toHaveBeenCalledWith(
      { name: "Den", expectedName: "Studio PC", libraryEpoch: "local-epoch" },
      { showErrorToast: false },
    );
  });
});

describe("device map: a request that ended", () => {
  it("stays on the map with its outcome until it is dismissed", async () => {
    managed = servers({
      ...managed,
      requests: [
        managementRequestOut("r-attic", {
          address: "http://192.168.1.90:34567",
          serverName: "Attic NAS",
          outcome: ManagedServerOutcome.RequestRejected,
          active: false,
        }),
      ],
    });
    vi.mocked(managedServerApi.cancelRequest).mockImplementation(async () => {
      managed = servers({ ...managed, requests: [] });

      return { changed: true };
    });
    renderPage();
    await loaded();
    const attic = node("server-request:r-attic");

    expect(attic.getAttribute("aria-label")).toContain("federation.map.issue.requestEnded");
    // The card is sized for what it says under its name, which is what became of the request.
    expect(Array.from(attic.querySelectorAll("text")).map((text) => text.textContent)).toContain(
      "federation.map.issue.requestEnded",
    );
    expect(document.querySelector('[data-edge="management:server-request:r-attic"]')).toBeNull();

    fireEvent.click(attic);
    await waitFor(() =>
      expect(panel()).toHaveAttribute("data-selection", "node:server-request:r-attic"),
    );
    expect(panel()).toHaveTextContent("federation.error.ManagedServerRequestRejected");
    // It can be tried again from here.
    expect(screen.getByTestId("manage-form")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "federation.servers.dismiss" }));
    });

    expect(managedServerApi.cancelRequest).toHaveBeenCalledWith("r-attic");
    await waitFor(() => expect(node("server-request:r-attic")).toBeNull());
  });
});

describe("device map: following a device through an action", () => {
  const heading = () => within(panel()).getByRole("heading", { level: 2 });
  let keepFocus: () => void;

  // Every action's busy button is disabled while it runs: in a real browser the keyboard has
  // left it long before the listings are read again.
  beforeEach(() => {
    keepFocus = blurWhenDisabled();
  });
  afterEach(() => keepFocus());

  it("keeps what approving said when the request leaves the map, and the keyboard in the details", async () => {
    vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockImplementation(async () => {
      // An answer takes a moment, as over a network: the busy dialog has let go of focus.
      await new Promise((resolve) => setTimeout(resolve, 20));
      // Approved requests leave the listing; the device joins it once it collects its key.
      settings = access({ ...settings, pendingRequests: [] });

      return ok as never;
    });
    renderPage();
    await loaded();
    fireEvent.click(node("manager-request:m1"));
    const approve = await screen.findByRole("button", {
      name: "federation.management.requests.approve",
    });

    act(() => approve.focus());
    fireEvent.click(approve);
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    await waitFor(() => expect(node("manager-request:m1")).toBeNull());
    await waitFor(() => expect(panel()).toHaveAttribute("data-overview", "true"));
    expect(within(panel()).getByRole("status")).toHaveTextContent(
      "federation.management.requests.approved Guest",
    );
    await waitFor(() => expect(heading()).toHaveFocus());
  });

  it("moves to the device it let in once that device has collected its key", async () => {
    vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockImplementation(async () => {
      settings = access({ ...settings, pendingRequests: [] });
      // Collected a moment later, by the device itself — listed under the id approval named.
      setTimeout(() => {
        settings = access({
          ...settings,
          devices: [
            ...settings.devices,
            manager("d-guest", "Guest", { platform: RemoteDevicePlatform.Linux }),
          ],
        });
      }, 300);

      return { ...ok, data: { deviceId: "d-guest" } } as never;
    });
    renderPage();
    await loaded();
    fireEvent.click(node("manager-request:m1"));
    const approve = await screen.findByRole("button", {
      name: "federation.management.requests.approve",
    });

    act(() => approve.focus());
    fireEvent.click(approve);
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    // Waiting for it: what approving said stays, and nothing lets go of it.
    await waitFor(() => expect(node("manager-request:m1")).toBeNull());
    expect(within(panel()).getByRole("status")).toHaveTextContent(
      "federation.management.requests.approved Guest",
    );
    // Then the device, and the same panel on it, keyboard and all.
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:manager:d-guest"), {
      timeout: 5000,
    });
    expect(edge("management:manager:d-guest")).toHaveAttribute("data-in", "active");
    expect(within(panel()).getByRole("status")).toHaveTextContent(
      "federation.management.requests.approved Guest",
    );
    expect(heading()).toHaveTextContent("Guest");
    await waitFor(() => expect(heading()).toHaveFocus());
  });

  it("never takes the keyboard back from where the reader put it while it waits for the device", async () => {
    // Approved, but the device never collects its key: the details wait, re-reading.
    vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockImplementation(async () => {
      settings = access({ ...settings, pendingRequests: [] });

      return { ...ok, data: { deviceId: "d-never" } } as never;
    });
    // Each reading is a new answer, as off the network: the page draws again every time.
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockImplementation(
      async () => ({ ...ok, data: structuredClone(settings) }) as never,
    );
    renderPage();
    await loaded();
    fireEvent.click(node("manager-request:m1"));
    const approve = await screen.findByRole("button", {
      name: "federation.management.requests.approve",
    });

    act(() => approve.focus());
    fireEvent.click(approve);
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });
    await waitFor(() => expect(node("manager-request:m1")).toBeNull());
    await waitFor(() => expect(heading()).toHaveFocus());

    // The reader clicks a part of the page that takes no focus: the keyboard is on the body.
    const reads = vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mock.calls.length;

    fireEvent.pointerDown(screen.getByTestId("device-map-legend"));
    act(() => heading().blur());
    expect(document.activeElement).toBe(document.body);
    // Re-read while waiting, twice — and focus stays where the reader left it.
    await waitFor(
      () =>
        expect(
          vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mock.calls.length,
        ).toBeGreaterThanOrEqual(reads + 2),
      { timeout: 6000 },
    );
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(document.activeElement).toBe(document.body);
    expect(within(panel()).getByRole("status")).toHaveTextContent(
      "federation.management.requests.approved Guest",
    );
  }, 10_000);

  it("gives the keyboard to the details' heading when what an action said is dismissed", async () => {
    vi.mocked(BApi.remoteAccess.approveRemoteDevicePairingRequest).mockImplementation(async () => {
      settings = access({ ...settings, pendingRequests: [] });

      return ok as never;
    });
    renderPage();
    await loaded();
    fireEvent.click(node("manager-request:m1"));
    fireEvent.click(
      await screen.findByRole("button", { name: "federation.management.requests.approve" }),
    );
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });
    const said = await waitFor(() => within(panel()).getByRole("status"));

    expect(said).toHaveTextContent("federation.management.requests.approved Guest");
    // The reader clicked elsewhere since, then came back to the message's × with the keyboard.
    fireEvent.pointerDown(screen.getByTestId("device-map-legend"));
    const dismiss = within(said).getByRole("button", { name: "federation.dismiss" });

    act(() => dismiss.focus());
    fireEvent.click(dismiss);

    await waitFor(() => expect(within(panel()).queryByRole("status")).toBeNull());
    expect(heading()).toHaveFocus();
  });
});

describe("device map: a managed server that moved", () => {
  beforeEach(() => {
    managed = servers({
      servers: [
        ...managed.servers,
        server("s4", {
          name: "Old NAS",
          address: "http://192.168.1.54:5000",
          state: ManagedServerState.WrongServer,
          answeredBy: { serverId: "other", name: "Other", isThisDevice: false },
        }),
      ],
    });
  });

  it("tells the way back in the map's own words", async () => {
    renderPage();
    await loaded();
    fireEvent.click(node("server:s4"));
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:s4"));

    const warning = screen.getByTestId("managed-server-wrong-server");

    expect(warning).toHaveTextContent("federation.map.panel.wrongServerTip");
    expect(warning).not.toHaveTextContent("federation.servers.wrongServerTip");
    // Not found yet: nothing to pair with, and never at the address someone else answers.
    expect(screen.queryByTestId("manage-form")).not.toBeInTheDocument();
  });

  /** Who answers where, as a probe finds it now. */
  const answers = (who: Record<string, string>) =>
    vi.mocked(managedServerApi.probe).mockImplementation(async (address: string) => ({
      outcome: who[address] ? ManagedServerOutcome.Ok : ManagedServerOutcome.Unreachable,
      serverId: who[address],
      pairingSupported: true,
      alreadyManaged: who[address] === "s4",
    }));
  const foundAt = (address: string) =>
    vi.mocked(managedServerApi.discover).mockResolvedValue({
      servers: [
        { serverId: "s4", name: "Old NAS", address, appVersion: "2.4.0", alreadyManaged: true },
      ],
    });
  const openMoved = async (options: { discover?: boolean; id?: string } = {}) => {
    const id = options.id ?? "server:s4";

    renderPage();
    await loaded();
    if (options.discover)
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "federation.servers.add.discover" }));
      });
    fireEvent.click(node(id));
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", `node:${id}`));
    await waitFor(() => expect(screen.queryByTestId("way-back-checking")).toBeNull());
  };

  it("offers to manage it again where it was found and answers as itself right now", async () => {
    foundAt("http://192.168.1.74:5000");
    answers({ "http://192.168.1.74:5000": "s4" });
    vi.mocked(managedServerApi.pair).mockResolvedValue({ outcome: 0, serverName: "Old NAS" });
    await openMoved({ discover: true });
    // Found by its id: the same device, not another one nearby.
    expect(document.querySelector('[data-ghost="true"]')).toBeNull();
    const form = screen.getByTestId("manage-form");

    expect(form).toHaveTextContent(
      "federation.map.panel.moved.title Old NAS http://192.168.1.74:5000",
    );
    expect(managedServerApi.probe).toHaveBeenCalledWith("http://192.168.1.74:5000");
    await act(async () => {
      fireEvent.submit(form);
    });

    // Asked once more right before: an address can change hands in between.
    expect(managedServerApi.probe).toHaveBeenCalledTimes(2);
    expect(managedServerApi.pair).toHaveBeenCalledWith("http://192.168.1.74:5000", undefined);
    expect(within(panel()).getByRole("status")).toHaveTextContent(
      "federation.servers.paired Old NAS",
    );
  });

  it("sends nothing when the address changed hands after it was found", async () => {
    foundAt("http://192.168.1.74:5000");
    answers({ "http://192.168.1.74:5000": "s4" });
    await openMoved({ discover: true });
    const form = screen.getByTestId("manage-form");

    answers({ "http://192.168.1.74:5000": "intruder" });
    await act(async () => {
      fireEvent.submit(form);
    });

    expect(managedServerApi.pair).not.toHaveBeenCalled();
    expect(within(panel()).getByRole("alert")).toHaveTextContent(
      "federation.map.panel.moved.gone Old NAS http://192.168.1.74:5000",
    );
    // Asked again: it is not there any more, so nothing is offered there.
    await waitFor(() => expect(screen.queryByTestId("manage-form")).not.toBeInTheDocument());
  });

  it("keeps the keyboard in the details when the address changed hands, through the search again", async () => {
    const keepFocus = blurWhenDisabled();

    try {
      foundAt("http://192.168.1.74:5000");
      answers({ "http://192.168.1.74:5000": "s4" });
      await openMoved({ discover: true });
      const form = screen.getByTestId("manage-form");
      const submit = within(form).getByRole("button", { name: "federation.servers.add.request" });

      // Another server answers there now; asking again elsewhere takes a moment, as over a
      // network, and finds it nowhere.
      vi.mocked(managedServerApi.probe).mockImplementation(async () => {
        await new Promise((resolve) => setTimeout(resolve, 30));

        return {
          outcome: ManagedServerOutcome.Ok,
          serverId: "intruder",
          pairingSupported: true,
          alreadyManaged: false,
        };
      });
      act(() => submit.focus());
      await act(async () => {
        fireEvent.submit(form);
      });

      expect(managedServerApi.pair).not.toHaveBeenCalled();
      await waitFor(() => expect(screen.queryByTestId("manage-form")).not.toBeInTheDocument());
      await waitFor(() => expect(screen.queryByTestId("way-back-checking")).toBeNull());
      expect(within(panel()).getByRole("alert")).toHaveTextContent(
        "federation.map.panel.moved.gone Old NAS http://192.168.1.74:5000",
      );
      await waitFor(() => expect(within(panel()).getByRole("heading", { level: 2 })).toHaveFocus());
    } finally {
      keepFocus();
    }
  });

  it("shows a request to manage it again where it is drawn, with its Cancel — and no second form", async () => {
    foundAt("http://192.168.1.74:5000");
    answers({ "http://192.168.1.74:5000": "s4" });
    managed = servers({
      ...managed,
      requests: [
        managementRequestOut("r-s4", {
          serverId: "s4",
          address: "http://192.168.1.74:5000",
          serverName: "Old NAS",
        }),
      ],
    });
    vi.mocked(managedServerApi.cancelRequest).mockImplementation(async () => {
      managed = servers({ ...managed, requests: [] });

      return { changed: true };
    });
    await openMoved({ discover: true });
    const waiting = screen.getByTestId("management-request-out");

    expect(waiting).toHaveTextContent("federation.servers.waiting Old NAS");
    expect(waiting).toHaveTextContent("http://192.168.1.74:5000");
    expect(screen.queryByTestId("manage-form")).not.toBeInTheDocument();
    await act(async () => {
      fireEvent.click(
        within(waiting).getByRole("button", { name: "federation.servers.cancelRequest" }),
      );
    });

    expect(managedServerApi.cancelRequest).toHaveBeenCalledWith("r-s4");
    // Cancelled: the way back is offered again.
    await waitFor(() => expect(screen.getByTestId("manage-form")).toBeInTheDocument());
  });

  it("keeps a refused request to manage it again, with its outcome and Dismiss", async () => {
    managed = servers({
      ...managed,
      requests: [
        managementRequestOut("r-s4", {
          serverId: "s4",
          address: "http://192.168.1.74:5000",
          serverName: "Old NAS",
          outcome: ManagedServerOutcome.RequestRejected,
          active: false,
        }),
      ],
    });
    vi.mocked(managedServerApi.cancelRequest).mockResolvedValue({ changed: true });
    await openMoved();

    expect(node("server:s4").getAttribute("aria-label")).toContain(
      "federation.map.issue.requestEnded",
    );
    const ended = screen.getByTestId("management-request-ended");

    expect(ended).toHaveTextContent("federation.error.ManagedServerRequestRejected");
    await act(async () => {
      fireEvent.click(within(ended).getByRole("button", { name: "federation.servers.dismiss" }));
    });
    expect(managedServerApi.cancelRequest).toHaveBeenCalledWith("r-s4");
  });
});

describe("device map: finding devices", () => {
  it("alone, explains the mode and offers to look around", async () => {
    sharing = status();
    managed = servers();
    settings = access();
    renderPage();
    const empty = await screen.findByTestId("device-map-empty");

    expect(empty).toHaveTextContent("federation.map.empty.body");
    await act(async () => {
      fireEvent.click(
        within(empty).getByRole("button", { name: "federation.servers.add.discover" }),
      );
    });

    expect(
      await within(screen.getByTestId("device-map-empty")).findByText(
        "federation.map.discovery.none",
      ),
    ).toBeInTheDocument();
  });
});

describe("device map: details on demand, below the widest windows", () => {
  const details = () => screen.queryByTestId("device-map-details");
  const layout = () => screen.getByTestId("device-map-layout");

  beforeEach(() => {
    narrowWindow();
  });

  it("gives the map the page's width until something is chosen, then sets the details beside it", async () => {
    renderPage();
    await loaded();

    expect(layout()).toHaveAttribute("data-layout", "on-demand");
    expect(layout()).toHaveAttribute("data-details", "closed");
    expect(details()).toBeNull();
    // One column, the map's.
    expect(layout()).not.toHaveClass("grid");
    // What the details would say with nothing selected, where they would say it.
    expect(screen.getByTestId("device-map-hint")).toHaveTextContent("federation.map.hint");

    const nas = node("server:nas");

    act(() => nas.focus());
    fireEvent.keyDown(nas, { key: "Enter" });
    await waitFor(() => expect(details()).toHaveAttribute("data-on-demand", "true"));
    // Beside the map, in the page's flow and never over it: a column of their own, taken from
    // the map's width — the map is laid out again for what is left.
    expect(layout()).toHaveAttribute("data-details", "open");
    expect(layout()).toHaveClass("grid");
    expect(layout().className).toMatch(/grid-cols-\[minmax\(0,1fr\)_[^\s\]]+\]/);
    expect(details()!.parentElement).toBe(layout());
    expect(details()!.className).not.toMatch(/\b(fixed|absolute)\b/);
    const heading = within(panel()).getByRole("heading", { level: 2 });

    expect(heading).toHaveTextContent("NAS");
    expect(heading).toHaveFocus();
    expect(screen.queryByTestId("device-map-hint")).toBeNull();

    // Escape closes them, the map has the whole width again, and the keyboard is back where
    // it was.
    fireEvent.keyDown(heading, { key: "Escape" });
    await waitFor(() => expect(details()).toBeNull());
    expect(layout()).toHaveAttribute("data-details", "closed");
    expect(layout()).not.toHaveClass("grid");
    expect(nas).toHaveFocus();
  });

  it("closes with its button, back to what opened it; the map stays usable meanwhile", async () => {
    renderPage();
    await loaded();
    const lap = node("server:lap");

    act(() => lap.focus());
    fireEvent.click(lap);
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:lap"));
    // Not modal: another device can be chosen with the details open.
    act(() => node("server:nas").focus());
    fireEvent.click(node("server:nas"));
    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:nas"));

    fireEvent.click(within(panel()).getByRole("button", { name: "federation.close" }));
    await waitFor(() => expect(details()).toBeNull());
    expect(node("server:nas")).toHaveFocus();
  });

  describe("with more devices than the width the details leave can draw", () => {
    /** What the browser tells the map when the width it is given changes. */
    const measured = new Set<() => void>();
    /** The map's width: all of the page's, or what the details leave it. */
    const widthOfMap = () => (layout().getAttribute("data-details") === "open" ? NARROWED : WHOLE);
    const WHOLE = 2400;
    const NARROWED = 700;
    const resized = () => act(() => measured.forEach((measure) => measure()));
    const mode = () => screen.getByTestId("device-map-canvas").getAttribute("data-mode");
    let measuring: MockInstance<(this: HTMLElement) => DOMRect> | undefined;

    beforeEach(() => {
      // Eight more devices: drawn across the page's width, listed in what the details leave.
      managed = servers({
        ...managed,
        servers: [
          ...managed.servers,
          ...Array.from({ length: 8 }, (_, i) =>
            server(`x${i}`, {
              name: `DESKTOP-${String(2000 + i)}QX`,
              address: `http://10.1.0.${i + 1}:34567`,
            }),
          ),
        ],
      });
      measured.clear();
      vi.stubGlobal(
        "ResizeObserver",
        class {
          private readonly measure: () => void;

          constructor(callback: () => void) {
            this.measure = callback;
          }

          observe() {
            measured.add(this.measure);
          }

          unobserve() {}

          disconnect() {
            measured.delete(this.measure);
          }
        },
      );
      const original = HTMLElement.prototype.getBoundingClientRect;

      measuring = vi.spyOn(HTMLElement.prototype, "getBoundingClientRect");
      measuring.mockImplementation(function (this: HTMLElement) {
        if (this.dataset.testid !== "device-map-canvas") return original.call(this);
        const width = widthOfMap();

        return {
          width,
          height: 600,
          top: 0,
          left: 0,
          right: width,
          bottom: 600,
          x: 0,
          y: 0,
        } as DOMRect;
      });
    });
    afterEach(() => {
      vi.unstubAllGlobals();
      measuring?.mockRestore();
    });

    it("gives the keyboard back to the device that opened them, drawn again once they close", async () => {
      renderPage();
      await loaded();
      expect(mode()).toBe("map");
      const nas = node("server:nas");

      act(() => nas.focus());
      fireEvent.keyDown(nas, { key: "Enter" });
      await waitFor(() => expect(details()).not.toBeNull());
      const heading = within(panel()).getByRole("heading", { level: 2 });

      expect(heading).toHaveFocus();
      // Beside the details the map is too narrow to draw: listed, and the keyboard stays in the
      // details, where the reader was.
      resized();
      expect(mode()).toBe("list");
      expect(nas.isConnected).toBe(false);
      expect(heading).toHaveFocus();

      // Escape closes them: the keyboard is on the device that opened them in the list…
      fireEvent.keyDown(heading, { key: "Escape" });
      await waitFor(() => expect(details()).toBeNull());
      expect(listed("server:nas")).toHaveFocus();
      // …and, once the map has its whole width back and is drawn again, on its card there.
      resized();
      expect(mode()).toBe("map");
      expect(node("server:nas")).toHaveFocus();
    });

    it("keeps the keyboard on the device it is on as the drawing and the list replace each other", async () => {
      renderPage();
      await loaded();
      const lap = node("server:lap");

      // A pointer: the card takes the keyboard, the details open and leave the map too narrow.
      act(() => lap.focus());
      fireEvent.pointerDown(lap);
      fireEvent.click(lap);
      await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:lap"));
      expect(lap).toHaveFocus();
      resized();
      expect(mode()).toBe("list");
      // The card went with the drawing: the keyboard is on the same device in the list.
      expect(listed("server:lap")).toHaveFocus();

      // Escape on the map lets go and closes the details; the map is drawn again, and the
      // keyboard is on the device's card — never on the page's body.
      fireEvent.keyDown(listed("server:lap"), { key: "Escape" });
      await waitFor(() => expect(details()).toBeNull());
      resized();
      expect(mode()).toBe("map");
      expect(node("server:lap")).toHaveFocus();
    });

    it("never takes the keyboard from where the reader put it", async () => {
      renderPage();
      await loaded();
      const lap = node("server:lap");

      act(() => lap.focus());
      fireEvent.click(lap);
      await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:server:lap"));
      // The reader moves on, to the refresh button: the map being listed does not pull the
      // keyboard back to it.
      const refresh = screen.getByRole("button", { name: "federation.refresh" });

      act(() => refresh.focus());
      resized();
      expect(mode()).toBe("list");
      expect(refresh).toHaveFocus();
    });
  });

  it("stays open on what an action said when the record it showed went away", async () => {
    const keepFocus = blurWhenDisabled();

    vi.mocked(BApi.remoteAccess.rejectRemoteDevicePairingRequest).mockImplementation(async () => {
      settings = access({ ...settings, pendingRequests: [] });

      return ok as never;
    });
    try {
      renderPage();
      await loaded();
      fireEvent.click(node("manager-request:m1"));
      const reject = await screen.findByRole("button", {
        name: "federation.management.requests.reject",
      });

      act(() => reject.focus());
      await act(async () => {
        fireEvent.click(reject);
      });

      await waitFor(() => expect(node("manager-request:m1")).toBeNull());
      await waitFor(() => expect(panel()).toHaveAttribute("data-overview", "true"));
      expect(details()).toHaveAttribute("data-on-demand", "true");
      expect(layout()).toHaveAttribute("data-details", "open");
      const heading = within(panel()).getByRole("heading", { level: 2 });

      await waitFor(() => expect(heading).toHaveFocus());
      // And closes like any other.
      fireEvent.keyDown(heading, { key: "Escape" });
      await waitFor(() => expect(details()).toBeNull());
    } finally {
      keepFocus();
    }
  });
});

describe("device map: data sync", () => {
  /**
   * The NAS kept in step both ways, a new PC asking to read this device's definitions, and this
   * device's own request to a garage server it knew nothing else about.
   */
  const garage = () =>
    mapPeer(
      "garage",
      "Garage",
      { id: 3, state: DataSyncLinkState.AwaitingAccess, peerMayReadUs: false },
      { request: ownRequest({ expiresAt: inTenMinutes() }) },
    );
  const syncing = (patch: Partial<DataSyncApi.DataSyncMapView> = {}) => {
    syncView = mapView({
      requests: [mapRequest("r1", "newpc", "New PC", { expiresAt: inTenMinutes() })],
      ...patch,
      peers: [...(patch.peers ?? [mapPeer("nas", "NAS")]), garage()],
    });
  };
  const synced = () => waitFor(() => expect(edge("sync:server:nas")).not.toBeNull());

  it("draws a line to each device it names, a claim on its own node, and this device's request", async () => {
    syncing();
    renderPage();
    await synced();

    expect(edge("sync:server:nas")).toHaveAttribute("data-kind", "sync");
    expect(edge("sync:server:nas")).toHaveAttribute("data-in", "active");
    expect(edge("sync:server:nas")).toHaveAttribute("data-out", "active");
    // Both ways in one state: one line with two arrowheads.
    expect(edge("sync:server:nas").querySelector("line[data-direction]")).toHaveAttribute(
      "data-direction",
      "both",
    );
    // The NAS it syncs with is the NAS it shares with and manages: one device.
    expect(document.querySelectorAll('[data-node="server:nas"][role="button"]')).toHaveLength(1);
    expect(edge("sync:sync-request:r1")).toHaveAttribute("data-out", "pending");
    expect(node("sync-request:r1").querySelector("[data-unverified-mark]")).not.toBeNull();
    expect(edge("sync:peer:garage")).toHaveAttribute("data-in", "pending");
    // The legend explains the line once there is one, and which way it points.
    expect(
      screen.getByTestId("device-map-legend").querySelector('[data-legend="sync"]'),
    ).not.toBeNull();
    expect(used.has("federation.map.legend.sync")).toBe(true);
    // In words too, direction by direction, with the mode.
    expect(
      within(screen.getByTestId("device-map-list")).getByText(
        /federation\.map\.direction\.sync\.in\.active NAS/,
      ),
    ).toHaveTextContent("federation.map.sync.mode.twoWay");
  });

  it("shows a claim only as its request, saying where it came from", async () => {
    syncing();
    renderPage();
    await synced();
    fireEvent.click(node("sync-request:r1"));

    await waitFor(() => expect(panel()).toHaveAttribute("data-selection", "node:sync-request:r1"));
    expect(screen.getByTestId("device-map-unverified")).toBeInTheDocument();
    const section = screen.getByTestId("device-map-sync-section");

    expect(within(section).getByTestId("data-sync-request-card")).toHaveTextContent(
      "dataSync.request.from 192.168.1.40",
    );
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
  });

  it("keeps what approving said when the claim leaves the map", async () => {
    syncing();
    vi.mocked(dataSyncApi.approveRequest).mockImplementation(async () => {
      syncView = mapView({ ...syncView, requests: [] });

      return { readBackGranted: false } as never;
    });
    renderPage();
    await synced();
    fireEvent.click(node("sync-request:r1"));
    fireEvent.click(await screen.findByTestId("data-sync-request-approve"));
    expect(
      within(dialog()).getByText(/dataSync\.request\.from 192\.168\.1\.40/),
    ).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog()).getByRole("button", { name: "federation.confirm" }));
    });

    await waitFor(() => expect(node("sync-request:r1")).toBeNull());
    expect(dataSyncApi.approveRequest).toHaveBeenCalledWith("r1", {
      receiveBack: false,
      kinds: undefined,
    });
    await waitFor(() =>
      expect(within(panel()).getByRole("status")).toHaveTextContent(
        "dataSync.request.approved New PC",
      ),
    );
  });

  it("shows this device's data sync with the rest of this device", async () => {
    syncing();
    renderPage();
    await synced();

    expect(within(panel()).getByTestId("data-sync-self-section")).toBeInTheDocument();
    expect(panel()).toHaveAttribute("data-overview", "true");
  });

  it("links to the data sync page from its header", async () => {
    renderPage();
    await loaded();

    expect(screen.getByRole("link", { name: "dataSync.title" })).toHaveAttribute(
      "href",
      "/data-sync",
    );
  });
});

describe("device map: failures", () => {
  it("names the remote-access mode this device is in", async () => {
    settings = access({ mode: RemoteAccessMode.Unrestricted });
    renderPage();
    await loaded();

    expect(screen.getByTestId("device-map-self-status")).toHaveTextContent(
      "federation.management.status.unrestricted",
    );
  });
});
