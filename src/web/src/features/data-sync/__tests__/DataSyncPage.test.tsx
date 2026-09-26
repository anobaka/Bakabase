import type * as Api from "../api";
import type { MockInstance } from "vitest";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DataSyncPage, { DATA_SYNC_ON_DEMAND_QUERY } from "../DataSyncPage";
import { dataSyncApi, DataSyncRequestError } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import {
  candidate,
  historyEntry,
  link,
  mapPeer,
  mapView,
  nameConflict,
  NOW,
  outgoing,
  overview,
  reader,
  request,
  status,
} from "./dataSyncFixtures";

import {
  ClientMode,
  DataSyncHistoryKind,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncProblemCode,
  DataSyncRequestDirection,
  DataSyncRestoreChoice,
  DataSyncStatusLevel,
  RemoteAccessMode,
} from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    overview: vi.fn(),
    links: vi.fn(),
    map: vi.fn(),
    requests: vi.fn(),
    readers: vi.fn(),
    peers: vi.fn(),
    entities: vi.fn(async () => []),
    updateLink: vi.fn(async () => ({})),
    setSharing: vi.fn(async () => undefined),
    createInvitation: vi.fn(),
    syncNow: vi.fn(async () => ({})),
    inbox: vi.fn(async () => ({ items: [], total: 0, openTotal: 0 })),
    history: vi.fn(async () => []),
    historyEntry: vi.fn(),
    restore: vi.fn(),
    chooseRestore: vi.fn(async () => ({ taskId: "DataSyncRestore" })),
    review: vi.fn(),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: { getAppInfo: vi.fn(async () => ({ code: 0, data: { backupPath: "/data/backups" } })) },
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));

const initialRemote = useRemoteAccessStore.getState();

/** A home office: a NAS in step both ways, a PC asked and waiting, a device that only reads. */
const homeOffice = () => {
  vi.mocked(dataSyncApi.overview).mockResolvedValue(
    overview({ status: status({ level: DataSyncStatusLevel.NeedsYou, openItems: 9 }) }),
  );
  vi.mocked(dataSyncApi.links).mockResolvedValue([
    link(1, "node-nas", "NAS", { openItems: 9 }),
    link(2, "node-pc2", "PC-2", {
      mode: DataSyncLinkMode.Follow,
      state: DataSyncLinkState.AwaitingAccess,
      peerMayReadUs: false,
      peerModeTowardsUs: undefined,
    }),
  ]);
  vi.mocked(dataSyncApi.map).mockResolvedValue(
    mapView({
      peers: [mapPeer("node-nas", "NAS")],
      outgoing: [outgoing(2, "node-pc2", "PC-2")],
    }),
  );
  vi.mocked(dataSyncApi.requests).mockResolvedValue([
    request("req-in-1", "node-newpc", "New PC"),
    request("req-out-1", "node-pc2", "PC-2", { direction: DataSyncRequestDirection.Outgoing }),
  ]);
  vi.mocked(dataSyncApi.readers).mockResolvedValue([
    reader("node-nas", "NAS"),
    reader("node-reader", "Reader PC", { mode: "follow" }),
  ]);
  vi.mocked(dataSyncApi.peers).mockResolvedValue([
    candidate("node-nas", "NAS", { weMayRead: true, theyMayRead: true, linkId: 1 }),
  ]);
};

const asWindow = (window: "local" | "console" | "unrestricted" | "lan" | "asking" | "unknown") => {
  if (window === "asking") {
    useRemoteAccessStore.setState({ initialized: false, context: "asking" });

    return;
  }
  if (window === "unknown") {
    useRemoteAccessStore.setState({ initialized: true, context: "unknown", isLocal: true });

    return;
  }
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: window === "local",
    clientMode:
      window === "console"
        ? ClientMode.PureClient
        : window === "local"
          ? ClientMode.AllInOne
          : ClientMode.RemoteBrowser,
    mode:
      window === "unrestricted"
        ? RemoteAccessMode.Unrestricted
        : window === "lan"
          ? RemoteAccessMode.Enabled
          : RemoteAccessMode.Disabled,
  });
};

const renderPage = (route = "/data-sync") =>
  render(
    <MemoryRouter initialEntries={[route]}>
      <Routes>
        <Route element={<DataSyncPage />} path="/data-sync" />
      </Routes>
    </MemoryRouter>,
  );

const loaded = () =>
  waitFor(() => expect(screen.getAllByTestId("data-sync-peer").length).toBeGreaterThan(0));
const spoke = (nodeId: string) =>
  document.querySelector<SVGGElement>(`[data-sync-peer="${nodeId}"][data-sync-part="spoke"]`)!;
const row = (nodeId: string) =>
  document.querySelector<HTMLButtonElement>(`[data-sync-peer="${nodeId}"][data-sync-part="row"]`)!;
const layout = () => screen.getByTestId("data-sync-layout");
const details = () => screen.queryByTestId("data-sync-details");

/** A window below 1536 px: the details are shown on demand, beside the diagram. */
let restoreWindow: (() => void) | undefined;
const narrowWindow = () => {
  const original = window.matchMedia;

  window.matchMedia = ((query: string) => ({
    ...original(query),
    matches: query === DATA_SYNC_ON_DEMAND_QUERY,
    media: query,
  })) as typeof window.matchMedia;
  restoreWindow = () => {
    window.matchMedia = original;
  };
};

beforeEach(() => {
  vi.clearAllMocks();
  // The fixtures' times are about NOW: a request that waits there waits here too.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(NOW);
  useDataSyncStore.getState().clear();
  asWindow("local");
  homeOffice();
});
afterEach(() => {
  vi.useRealTimers();
  cleanup();
  restoreWindow?.();
  restoreWindow = undefined;
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("who may use the page", () => {
  it("waits while the server has not said who is looking", () => {
    asWindow("asking");
    renderPage();

    expect(screen.getByTestId("data-sync-asking")).toBeInTheDocument();
    expect(dataSyncApi.overview).not.toHaveBeenCalled();
  });

  it("tells a browser on a server outside Unrestricted mode where to go, and asks nothing", () => {
    asWindow("lan");
    renderPage();

    expect(screen.getByTestId("data-sync-not-available")).toHaveTextContent(
      "dataSync.notAvailable",
    );
    expect(dataSyncApi.overview).not.toHaveBeenCalled();
    expect(dataSyncApi.links).not.toHaveBeenCalled();
  });

  it("tries when who is looking is unknown, and says the same once the server refuses", async () => {
    asWindow("unknown");
    vi.mocked(dataSyncApi.overview).mockRejectedValue(
      new DataSyncRequestError("HostOnly", "refused", 403),
    );
    for (const read of [
      dataSyncApi.links,
      dataSyncApi.map,
      dataSyncApi.requests,
      dataSyncApi.readers,
    ])
      vi.mocked(read).mockRejectedValue(new DataSyncRequestError("HostOnly", "refused", 403));
    renderPage();

    await waitFor(() => expect(screen.getByTestId("data-sync-not-available")).toBeInTheDocument());
    expect(screen.queryByTestId("data-sync-error")).toBeNull();
  });

  it("works in the desktop app's window showing a server it manages", async () => {
    asWindow("console");
    renderPage();
    await loaded();

    expect(screen.getByTestId("data-sync-sharing-switch")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-request-approve")).toBeInTheDocument();
  });
});

describe("the page", () => {
  it("draws the devices first, with the page's help and status", async () => {
    renderPage();
    await loaded();

    expect(screen.getByTestId("data-sync-page")).toBeInTheDocument();
    expect(screen.getByTestId("help")).toHaveAttribute("data-help", "multiDevice/dataSync");
    expect(screen.getByTestId("data-sync-overall-status")).toHaveTextContent(
      "dataSync.status.NeedsYou 9",
    );
    expect(screen.getByTestId("data-sync-diagram")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-self")).toHaveTextContent("This PC");
    expect(
      screen.getAllByTestId("data-sync-peer").map((card) => card.getAttribute("data-sync-peer")),
    ).toEqual(["node-nas", "node-pc2", "node-reader"]);
    expect(screen.getAllByTestId("data-sync-spoke")).toHaveLength(3);
    // The requests, both ways, and this device's own side.
    expect(screen.getByTestId("data-sync-request-card")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-outgoing-card")).toHaveAttribute(
      "data-outcome",
      "awaitingApproval",
    );
    expect(screen.getByTestId("data-sync-this-device")).toBeInTheDocument();
    expect(
      within(screen.getByTestId("data-sync-readers")).getByText("Reader PC"),
    ).toBeInTheDocument();
  });

  it("lists only requests that still wait: the server keeps the decided ones too", async () => {
    vi.mocked(dataSyncApi.requests).mockResolvedValue([
      request("req-in-1", "node-newpc", "New PC", { status: "granted" }),
      request("req-in-2", "node-old", "Old PC", { status: "rejected" }),
      request("req-in-3", "node-late", "Late PC", { expiresAt: "2026-09-01 07:00:00.000" }),
      request("req-out-1", "node-pc2", "PC-2", {
        direction: DataSyncRequestDirection.Outgoing,
        status: "granted",
      }),
    ]);
    vi.mocked(dataSyncApi.map).mockResolvedValue(mapView({ peers: [mapPeer("node-nas", "NAS")] }));
    renderPage();
    await loaded();

    expect(screen.queryByTestId("data-sync-request-card")).toBeNull();
    expect(screen.queryByTestId("data-sync-outgoing-card")).toBeNull();
    expect(screen.queryByTestId("data-sync-requests")).toBeNull();
  });

  it("says on a request that approving replaces the access of a device already reading under its id", async () => {
    vi.mocked(dataSyncApi.requests).mockResolvedValue([
      request("req-in-1", "node-nas", "NAS", { replacesExistingAccess: true }),
      request("req-in-2", "node-newpc", "New PC"),
    ]);
    renderPage();
    await loaded();

    const cards = screen.getAllByTestId("data-sync-request-card");

    expect(cards.map((card) => card.getAttribute("data-request"))).toEqual([
      "req-in-1",
      "req-in-2",
    ]);
    expect(within(cards[0]).getByTestId("data-sync-request-replaces")).toBeInTheDocument();
    expect(within(cards[1]).queryByTestId("data-sync-request-replaces")).toBeNull();
  });

  it("docks the details beside the diagram at 1536 px and wider", async () => {
    renderPage();
    await loaded();

    expect(layout()).toHaveAttribute("data-layout", "docked");
    expect(layout()).toHaveAttribute("data-details", "open");
    expect(screen.getByTestId("data-sync-self-summary")).toBeInTheDocument();
    fireEvent.click(spoke("node-nas"));
    expect(within(details()!).getByTestId("data-sync-link-details")).toHaveAttribute(
      "data-peer",
      "node-nas",
    );
    expect(within(details()!).getByTestId("data-sync-rule-drawing")).toBeInTheDocument();
  });

  it("opens a link's details from the address", async () => {
    narrowWindow();
    renderPage("/data-sync?link=2");
    await loaded();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-link-details")).toHaveAttribute("data-peer", "node-pc2"),
    );
  });

  it("opens the wizard from the address", async () => {
    vi.mocked(dataSyncApi.peers).mockResolvedValue([]);
    renderPage("/data-sync?add=1");

    await waitFor(() => expect(screen.getByTestId("data-sync-wizard")).toBeInTheDocument());
  });

  it("hides the controls that create access where the server says this caller may not", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ canManageSharing: false }));
    renderPage();
    await loaded();

    expect(screen.queryByTestId("data-sync-sharing-switch")).toBeNull();
    expect(screen.queryByTestId("data-sync-create-code")).toBeNull();
    expect(screen.queryByTestId("data-sync-request-approve")).toBeNull();
    // What reduces access stays.
    expect(screen.getByTestId("data-sync-sharing-off")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-request-reject")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-pause-all")).toBeInTheDocument();
    expect(screen.getAllByText("dataSync.manageElsewhere").length).toBeGreaterThan(0);
  });

  it("changes sharing new definitions alone, never sending back what it read of sharing", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ canManageSharing: false, sharingEnabled: true, newDefinitionsStayLocal: false }),
    );
    renderPage();
    await loaded();

    const shareNew = screen.getByTestId("data-sync-new-definitions");

    // Open to a window that may not create access: it neither turns sharing on nor widens it.
    expect(shareNew).toBeEnabled();
    await act(async () => {
      fireEvent.click(shareNew);
    });
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enablePairedRemoteAccess: false,
      newDefinitionsStayLocal: true,
    });
    expect(vi.mocked(dataSyncApi.setSharing).mock.calls[0][0]).not.toHaveProperty("enabled");
  });

  it("hides them from an Unrestricted browser before the server says so", async () => {
    asWindow("unrestricted");
    renderPage();
    await loaded();

    expect(screen.queryByTestId("data-sync-sharing-switch")).toBeNull();
    expect(screen.queryByTestId("data-sync-create-code")).toBeNull();
    expect(screen.queryByTestId("data-sync-request-approve")).toBeNull();
  });

  it("asks before turning sharing on with remote access off, and says it turns that on too", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ sharingEnabled: false, remoteAccessMode: RemoteAccessMode.Disabled }),
    );
    renderPage();
    await loaded();

    fireEvent.click(screen.getByTestId("data-sync-sharing-switch"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("dataSync.sharing.remoteAccess");
    await act(async () => {
      fireEvent.click(within(dialog).getByText("federation.confirm"));
    });
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
  });

  it("asks what wins after a restore, naming the evidence", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ restorePending: true }));
    vi.mocked(dataSyncApi.restore).mockResolvedValue({
      pending: true,
      reason: DataSyncPauseReason.LocalRestoreDetected,
      detectedAt: "2026-09-01 07:40:00.000",
      pausedLinks: 2,
      evidenceFromName: "NAS",
    });
    renderPage();
    await loaded();

    const panel = await screen.findByTestId("data-sync-restore-pending");

    expect(panel).toHaveAttribute("data-evidence", "both");
    expect(panel).toHaveTextContent("dataSync.restore.intro");
    expect(within(panel).getByTestId("data-sync-restore-evidence")).toHaveTextContent(
      "dataSync.restore.evidence.both NAS",
    );
    fireEvent.click(within(panel).getByTestId("data-sync-restore-this-device"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("dataSync.restore.confirm.thisDevice");
    await act(async () => {
      fireEvent.click(within(dialog).getByText("federation.confirm"));
    });
    expect(dataSyncApi.chooseRestore).toHaveBeenCalledWith(
      DataSyncRestoreChoice.ThisDeviceWins,
      undefined,
    );
  });

  it("names the one device when a restore is only suspected through it", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ restorePending: true }));
    vi.mocked(dataSyncApi.restore).mockResolvedValue({
      pending: true,
      reason: DataSyncPauseReason.LocalRestoreSuspected,
      pausedLinks: 1,
      linkId: 1,
      evidenceFromName: "NAS",
    });
    renderPage();
    await loaded();

    const panel = await screen.findByTestId("data-sync-restore-pending");

    expect(panel).toHaveAttribute("data-evidence", "peer");
    expect(within(panel).getByTestId("data-sync-restore-evidence")).toHaveTextContent(
      "dataSync.restore.evidence.peer NAS",
    );
    fireEvent.click(within(panel).getByTestId("data-sync-restore-others"));
    await act(async () => {
      fireEvent.click(within(screen.getByRole("alertdialog")).getByText("federation.confirm"));
    });
    expect(dataSyncApi.chooseRestore).toHaveBeenCalledWith(DataSyncRestoreChoice.OthersWin, 1);
  });

  it("keeps the panel to one line when the reader decides later, and says so when nothing waits", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ restorePending: true }));
    vi.mocked(dataSyncApi.restore).mockResolvedValue({
      pending: true,
      reason: DataSyncPauseReason.LocalRestoreDetected,
      pausedLinks: 2,
    });
    renderPage();
    await loaded();
    const panel = await screen.findByTestId("data-sync-restore-pending");

    expect(panel).toHaveAttribute("data-evidence", "own");
    fireEvent.click(within(panel).getByTestId("data-sync-restore-later"));
    expect(screen.getByTestId("data-sync-restore-pending")).toHaveTextContent(
      "dataSync.restore.pending",
    );
    expect(dataSyncApi.chooseRestore).not.toHaveBeenCalled();
    cleanup();

    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview());
    vi.mocked(dataSyncApi.restore).mockResolvedValue({ pending: false, pausedLinks: 0 });
    renderPage("/data-sync?restore=1");
    await loaded();
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-restore-panel")).toHaveTextContent(
        "dataSync.restore.nothing",
      ),
    );
  });

  it("opens a link's first sync review from the address", async () => {
    vi.mocked(dataSyncApi.links).mockResolvedValue([
      link(3, "node-laptop", "Laptop", {
        state: DataSyncLinkState.AwaitingReview,
        reviewId: "review-1",
      }),
    ]);
    vi.mocked(dataSyncApi.review).mockResolvedValue({
      copyOnce: false,
      linkMode: DataSyncLinkMode.TwoWay,
      problem: { code: DataSyncProblemCode.ReviewExpired },
    });
    renderPage("/data-sync?link=3&review=1");

    await waitFor(() => expect(screen.getByTestId("data-sync-review")).toBeInTheDocument());
    expect(dataSyncApi.review).toHaveBeenCalledWith("review-1");
    fireEvent.click(
      within(screen.getByTestId("data-sync-review")).getAllByText("dataSync.close")[0],
    );
    await waitFor(() => expect(screen.queryByTestId("data-sync-review")).toBeNull());
  });

  it("brings Needs you forward from the address, on the device asked for", async () => {
    vi.mocked(dataSyncApi.inbox).mockResolvedValue({
      items: [nameConflict(1)],
      total: 1,
      openTotal: 1,
    });
    renderPage("/data-sync?tab=inbox&peer=node-nas");
    await loaded();

    await waitFor(() => expect(screen.getByTestId("data-sync-inbox")).toBeInTheDocument());
    expect(screen.getByTestId("data-sync-inbox-filter-device")).toHaveValue("node-nas");
    expect(screen.getAllByTestId("data-sync-inbox-card")).toHaveLength(1);
  });

  it("lists the history below the requests", async () => {
    vi.mocked(dataSyncApi.history).mockResolvedValue([
      historyEntry(1, DataSyncHistoryKind.AutoSync),
    ]);
    renderPage();
    await loaded();

    await waitFor(() => expect(screen.getAllByTestId("data-sync-history-entry")).toHaveLength(1));
    expect(screen.getByTestId("data-sync-history-drawing")).toBeInTheDocument();
  });

  it("takes a link's details to the history with that device alone", async () => {
    vi.mocked(dataSyncApi.history).mockResolvedValue([
      historyEntry(1, DataSyncHistoryKind.AutoSync),
      historyEntry(2, DataSyncHistoryKind.FirstLink, {
        linkId: 2,
        peerNodeId: "node-pc2",
        peerName: "PC-2",
      }),
    ]);
    renderPage("/data-sync?link=1");
    await loaded();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-history-entry")).toHaveLength(2));
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-link-details")).toHaveAttribute("data-peer", "node-nas"),
    );

    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-link-history"));
      await new Promise((resolve) => setTimeout(resolve));
    });
    const history = screen.getByTestId("data-sync-history");

    expect(within(history).getAllByTestId("data-sync-history-entry")).toHaveLength(1);
    expect(within(history).getByTestId("data-sync-history-entry")).toHaveAttribute(
      "data-entry",
      "1",
    );
    expect(within(history).getByTestId("data-sync-history-filter-device")).toHaveValue("node-nas");
    expect(within(history).getByRole("heading", { name: "dataSync.history.title" })).toHaveFocus();

    // Every device's again, from the filter.
    fireEvent.change(within(history).getByTestId("data-sync-history-filter-device"), {
      target: { value: "" },
    });
    expect(within(history).getAllByTestId("data-sync-history-entry")).toHaveLength(2);
  });

  it("keeps each source on its own: one that fails leaves the others", async () => {
    vi.mocked(dataSyncApi.readers).mockRejectedValue(
      new DataSyncRequestError("Http500", "boom", 500),
    );
    renderPage();
    await loaded();

    expect(
      within(screen.getByTestId("data-sync-readers")).getByTestId("data-sync-error"),
    ).toHaveTextContent("boom");
    expect(screen.getByTestId("data-sync-request-card")).toBeInTheDocument();
  });
});

describe("the details beside the diagram, shown on demand", () => {
  /** What the browser tells the diagram when the width it is given changes. */
  const measured = new Set<() => void>();
  const resized = () => act(() => measured.forEach((measure) => measure()));
  let window_ = 1440;
  /** The diagram's width: the page's, less the navigation and, while they are open, the details. */
  const widthOfDiagram = () =>
    window_ - 240 - 48 - (layout().getAttribute("data-details") === "open" ? 380 + 12 : 0);
  let measuring: MockInstance<(this: HTMLElement) => DOMRect> | undefined;

  beforeEach(() => {
    narrowWindow();
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
      if (this.dataset.testid !== "data-sync-diagram") return original.call(this);
      const width = widthOfDiagram();

      return {
        width,
        height: 400,
        top: 0,
        left: 0,
        right: width,
        bottom: 400,
        x: 0,
        y: 0,
      } as DOMRect;
    });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    measuring?.mockRestore();
  });

  it.each([1280, 1440])(
    "at %i px open from a spoke beside the diagram, never over it, and Escape gives the spoke the keyboard back",
    async (width) => {
      window_ = width;
      renderPage();
      await loaded();
      resized();
      expect(layout()).toHaveAttribute("data-layout", "on-demand");
      expect(layout()).toHaveAttribute("data-details", "closed");
      expect(details()).toBeNull();
      expect(screen.getByTestId("data-sync-hint")).toBeInTheDocument();

      const nas = spoke("node-nas");

      act(() => nas.focus());
      fireEvent.keyDown(nas, { key: "Enter" });
      await waitFor(() => expect(details()).not.toBeNull());
      const heading = within(details()!).getByRole("heading", { level: 2 });

      expect(heading).toHaveFocus();
      expect(heading).toHaveTextContent("NAS");
      // Beside the diagram, in the layout's own column: not laid over it.
      expect(layout()).toHaveAttribute("data-details", "open");
      expect(details()!.parentElement).toBe(layout());
      expect(screen.getByTestId("data-sync-diagram-region").parentElement).toBe(layout());
      expect(layout().className).toContain("grid-cols-");

      // The diagram is laid out again for the width that is left: every device still shown,
      // drawn or listed.
      resized();
      const mode = screen.getByTestId("data-sync-diagram").getAttribute("data-mode");

      expect(mode).toBe(width === 1280 ? "list" : "drawing");
      for (const nodeId of ["node-nas", "node-pc2", "node-reader"])
        expect(document.querySelector(`[data-sync-peer="${nodeId}"]`)).not.toBeNull();

      fireEvent.keyDown(heading, { key: "Escape" });
      await waitFor(() => expect(details()).toBeNull());
      if (width === 1280) {
        // Still listed until the diagram has its width back: the keyboard is on the device's row…
        expect(row("node-nas")).toHaveFocus();
        resized();
      }
      // …and on its spoke once the diagram is drawn again.
      expect(screen.getByTestId("data-sync-diagram")).toHaveAttribute("data-mode", "drawing");
      expect(spoke("node-nas")).toHaveFocus();
    },
  );

  it("closes with its button, the keyboard going back to the card that opened them", async () => {
    window_ = 1440;
    renderPage();
    await loaded();
    resized();
    const card = document.querySelector<SVGGElement>(
      '[data-sync-peer="node-pc2"][data-sync-part="card"]',
    )!;

    act(() => card.focus());
    fireEvent.keyDown(card, { key: " " });
    await waitFor(() => expect(details()).not.toBeNull());
    fireEvent.click(screen.getByTestId("data-sync-details-close"));
    await waitFor(() => expect(details()).toBeNull());
    expect(card).toHaveFocus();
  });
});

describe("the details on a narrow page", () => {
  /** What the browser tells the page and the diagram when the width they are given changes. */
  const measured = new Set<() => void>();
  const resized = () => act(() => measured.forEach((measure) => measure()));
  /** A phone: the page's content is 263 px wide once the navigation stands beside it. */
  const pageWidth = 263;
  let measuring: MockInstance<(this: HTMLElement) => DOMRect> | undefined;
  const scrolled = vi.fn();
  const originalScroll = HTMLElement.prototype.scrollIntoView;

  beforeEach(() => {
    narrowWindow();
    measured.clear();
    scrolled.mockClear();
    HTMLElement.prototype.scrollIntoView = scrolled;
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
      const testId = this.dataset.testid;

      if (testId !== "data-sync-layout" && testId !== "data-sync-diagram")
        return original.call(this);

      return {
        width: pageWidth,
        height: 400,
        top: 0,
        left: 0,
        right: pageWidth,
        bottom: 400,
        x: 0,
        y: 0,
      } as DOMRect;
    });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    measuring?.mockRestore();
    HTMLElement.prototype.scrollIntoView = originalScroll;
  });

  it("opens them under the diagram, never beside it, and gives the row the keyboard back", async () => {
    renderPage();
    // Measured as it mounts: listed from the start.
    await waitFor(() =>
      expect(screen.getAllByTestId("data-sync-peer-row").length).toBeGreaterThan(0),
    );
    resized();
    expect(layout()).toHaveAttribute("data-layout", "stacked");
    expect(screen.getByTestId("data-sync-diagram")).toHaveAttribute("data-mode", "list");

    const nas = row("node-nas");

    act(() => nas.focus());
    fireEvent.click(nas, { detail: 0 });
    await waitFor(() => expect(details()).not.toBeNull());
    const heading = within(details()!).getByRole("heading", { level: 2 });

    expect(heading).toHaveFocus();
    // Not a column beside the diagram: a block of its own after it, the page's whole width.
    expect(layout()).toHaveAttribute("data-details", "open");
    expect(layout().className).not.toContain("grid-cols-");
    expect(
      screen.getByTestId("data-sync-diagram-region").compareDocumentPosition(details()!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(details()!.className).not.toContain("sticky");
    expect(scrolled.mock.contexts).toContain(details());

    fireEvent.keyDown(heading, { key: "Escape" });
    await waitFor(() => expect(details()).toBeNull());
    expect(row("node-nas")).toHaveFocus();
  });
});
