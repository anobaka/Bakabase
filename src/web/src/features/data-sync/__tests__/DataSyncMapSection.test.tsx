import type * as Api from "../api";
import type { DataSyncMapSectionNode } from "../map/DataSyncMapSection";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DataSyncMapSection from "../map/DataSyncMapSection";
import DataSyncGhostAction from "../map/DataSyncGhostAction";
import DataSyncSelfSection from "../map/DataSyncSelfSection";
import { DataSyncProblemError, dataSyncApi } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import {
  mapPeer,
  mapRequest,
  mapView,
  minutesAgo,
  NOW,
  overview,
  ownRequest,
  recordingActions,
} from "./dataSyncFixtures";

import { MessageError } from "@/features/federation/components/common";
import {
  ClientMode,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncProblemCode,
  DataSyncRequestIntent,
  RemoteAccessMode,
} from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      // How English joins the parts of a label, so a label reads as it will.
      (
        ({
          "dataSync.a11y.sentenceBreak": ". ",
        }) as Record<string, string | undefined>
      )[key] ??
      (options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key),
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/stores/remoteAccess", () => ({
  // This device's own window, as the device map always is.
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: true, isLocal: true, clientMode: ClientMode.AllInOne }),
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    overview: vi.fn(),
    createLink: vi.fn(async () => ({})),
    updateLink: vi.fn(async () => ({})),
    copyOnce: vi.fn(async () => ({})),
    resetLink: vi.fn(async () => undefined),
    cancelRequest: vi.fn(async () => undefined),
    approveRequest: vi.fn(async () => ({ readBackGranted: false })),
    rejectRequest: vi.fn(async () => undefined),
    setSharing: vi.fn(async () => undefined),
    revokeReader: vi.fn(async () => undefined),
    createInvitation: vi.fn(),
  },
}));

let recorded = recordingActions();
const confirmation = () => recorded.confirmations[recorded.confirmations.length - 1];

/** A device on the map, as the map's `MapNode` carries it. */
const mapNode = (patch: Partial<DataSyncMapSectionNode> = {}): DataSyncMapSectionNode => ({
  id: "peer:nas",
  name: "NAS",
  address: "http://192.168.1.20:34567",
  unverified: false,
  keys: ["address:192.168.1.20:34567", "id:nas"],
  issues: [],
  ...patch,
  sources: { syncRequests: [], ...patch.sources },
});

const section = (node: DataSyncMapSectionNode, view = mapView()) =>
  render(
    <MemoryRouter>
      <DataSyncMapSection
        actions={recorded.actions}
        node={node}
        now={NOW}
        selfName="Studio PC"
        view={view}
      />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  recorded = recordingActions();
  useDataSyncStore.getState().clear();
  vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ deviceName: "Studio PC" }));
});
afterEach(cleanup);

describe("data sync in the device map's details", () => {
  it("shows a claim only as its request, never with the rule editor", () => {
    section(
      mapNode({
        id: "sync-request:r1",
        name: "New PC",
        unverified: true,
        keys: ["request:r1"],
        sources: {
          syncRequests: [
            mapRequest("r1", "newpc", "New PC", { intent: DataSyncRequestIntent.TwoWay }),
          ],
        },
      }),
    );

    expect(screen.getByTestId("device-map-sync-section")).toBeInTheDocument();
    const card = screen.getByTestId("data-sync-request-card");

    expect(card).toHaveTextContent("dataSync.request.from 192.168.1.40");
    // Its approval options are inline: a confirmation shows text only.
    expect(within(card).getByTestId("data-sync-request-options")).toBeInTheDocument();
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
    expect(screen.queryByTestId("data-sync-start")).toBeNull();
  });

  it("says, on a claim, that approving replaces the access of a device that reads under its id", () => {
    section(
      mapNode({
        id: "sync-request:r1",
        name: "NAS",
        unverified: true,
        keys: ["request:r1"],
        sources: {
          syncRequests: [mapRequest("r1", "nas", "NAS", { replacesExistingAccess: true })],
        },
      }),
    );

    expect(screen.getByTestId("data-sync-request-replaces")).toHaveTextContent(
      "dataSync.request.replacesExisting",
    );
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).toContain("dataSync.request.replacesExisting");
  });

  it("shows nothing for a claim that asks something else", () => {
    const { container } = section(mapNode({ unverified: true, keys: ["request:s1"] }));

    expect(container).toBeEmptyDOMElement();
  });

  it("shows the rule editor for a device this one syncs with, named as the map names it", () => {
    section(
      mapNode({
        name: "Attic NAS",
        sources: { sync: mapPeer("nas", "nas-01"), syncRequests: [] },
      }),
    );

    expect(screen.getByTestId("data-sync-rule-drawing")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-arrow-receive")).toHaveAccessibleName(
      "federation.map.direction.sync.in.active Attic NAS",
    );
    // The link's own details are a step away, on the page.
    expect(screen.getByText("dataSync.link.openPage").closest("a")).toHaveAttribute(
      "href",
      "/data-sync?link=1",
    );
    expect(screen.queryByTestId("data-sync-outgoing-card")).toBeNull();
  });

  it("names what is not synced with it, and shows it on the page", () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer("nas", "NAS", { excludedCount: 3, heldCount: 2 }),
          syncRequests: [],
        },
      }),
    );

    const counts = screen.getByTestId("data-sync-not-synced");

    expect(counts.querySelector('[data-count="skipped"]')).toHaveTextContent(
      "dataSync.link.skipped 3",
    );
    expect(
      within(counts.querySelector<HTMLElement>('[data-count="skipped"]')!)
        .getByText("dataSync.link.show")
        .closest("a"),
    ).toHaveAttribute("href", "/data-sync?link=1");
    expect(counts.querySelector('[data-count="withheld"]')).toHaveTextContent(
      "dataSync.link.withheld 2",
    );
  });

  it("says nothing of what is not synced where everything is", () => {
    section(mapNode({ sources: { sync: mapPeer("nas", "NAS"), syncRequests: [] } }));

    expect(screen.queryByTestId("data-sync-not-synced")).toBeNull();
  });

  it("offers to start without the other device's first review once the map says it may", () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer("nas", "NAS", {
            state: DataSyncLinkState.WaitingForPeerReview,
            lastSyncedAt: undefined,
            startAnywayAt: minutesAgo(1),
          }),
          syncRequests: [],
        },
      }),
    );

    expect(screen.getByTestId("data-sync-start-anyway")).toHaveTextContent(
      "dataSync.pause.startAnyway",
    );
  });

  it("turns receiving off through the map's confirmation", async () => {
    section(mapNode({ sources: { sync: mapPeer("nas", "NAS"), syncRequests: [] } }));

    fireEvent.click(screen.getByTestId("data-sync-mode-off"));
    expect(recorded.actions.confirm).toHaveBeenCalledTimes(1);
    expect(confirmation()).toMatchObject({
      title: "dataSync.off.title NAS",
      warning: "dataSync.off.stillReads NAS",
      refresh: ["dataSync"],
    });
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Off });
  });

  it("says a failure in data sync's words, for the map to show as it is", async () => {
    vi.mocked(dataSyncApi.updateLink).mockRejectedValueOnce(
      new DataSyncProblemError({ code: DataSyncProblemCode.Busy }),
    );
    section(mapNode({ sources: { sync: mapPeer("nas", "NAS"), syncRequests: [] } }));

    fireEvent.click(screen.getByTestId("data-sync-mode-off"));
    const failure = await confirmation()
      .action()
      .catch((cause: unknown) => cause);

    expect(failure).toBeInstanceOf(MessageError);
    expect((failure as Error).message).toBe("dataSync.problem.Busy");
  });

  it("shows this device's own request while it waits, with Cancel, and the editor only once approved", async () => {
    // A request no link carries (one left after a Reset): the request alone.
    const waiting = mapPeer("nas", "NAS", null, {
      reader: undefined,
      request: ownRequest({ requestId: "req-out-7" }),
    });

    section(mapNode({ sources: { sync: waiting, syncRequests: [] } }));

    const card = screen.getByTestId("data-sync-outgoing-card");

    expect(card).toHaveAttribute("data-outcome", "awaitingApproval");
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
    await act(async () => {
      fireEvent.click(within(card).getByTestId("data-sync-outgoing-cancel"));
    });
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    // Withdraws the request its record names: never resets a link.
    expect(dataSyncApi.cancelRequest).toHaveBeenCalledWith("req-out-7");
    expect(dataSyncApi.resetLink).not.toHaveBeenCalled();
  });

  it("offers no Cancel for a link whose request is no longer listed", () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer(
            "nas",
            "NAS",
            { id: 3, state: DataSyncLinkState.AwaitingAccess, peerMayReadUs: false },
            { request: ownRequest({ requestId: undefined }) },
          ),
          syncRequests: [],
        },
      }),
    );

    expect(screen.getByTestId("data-sync-outgoing-card")).toBeInTheDocument();
    expect(screen.queryByTestId("data-sync-outgoing-cancel")).toBeNull();
  });

  it("offers data sync's help in its heading", () => {
    section(mapNode({ sources: { sync: mapPeer("nas", "NAS"), syncRequests: [] } }));

    expect(
      within(screen.getByTestId("device-map-sync-section")).getByTestId("help"),
    ).toHaveAttribute("data-help", "multiDevice/dataSync");
  });

  it("shows the request beside the editor where the device already reads this one", () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer(
            "nas",
            "NAS",
            { id: 3, state: DataSyncLinkState.AwaitingAccess },
            { request: ownRequest() },
          ),
          syncRequests: [],
        },
      }),
    );

    expect(screen.getByTestId("data-sync-outgoing-card")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-arrow-receive")).toHaveAttribute("data-status", "pending");
  });

  it("keeps a request that ended, with Dismiss, until it is dismissed", async () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer(
            "nas",
            "NAS",
            { id: 3, mode: DataSyncLinkMode.Off, state: DataSyncLinkState.Stopped },
            { request: ownRequest({ requestId: undefined, outcome: "rejected" }) },
          ),
          syncRequests: [],
        },
      }),
    );

    expect(screen.getByTestId("data-sync-outgoing-card")).toHaveAttribute(
      "data-outcome",
      "rejected",
    );
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-outgoing-dismiss"));
    });
    expect(dataSyncApi.resetLink).toHaveBeenCalledWith(3);
  });

  it("offers to start with a device it knows, sending the request to where the map knows it", async () => {
    section(mapNode());

    expect(screen.getByText("dataSync.map.none NAS")).toBeInTheDocument();
    const toggle = screen.getByTestId("data-sync-start-toggle");

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    // Off, nothing sent yet.
    expect(screen.getByTestId("data-sync-arrow-receive")).toHaveAttribute("data-status", "none");
    expect(dataSyncApi.createLink).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    // Asking creates access: the request the device will see is previewed first.
    expect(confirmation()).toMatchObject({
      title: "dataSync.follow.title NAS",
      warning: "dataSync.request.follow Studio PC",
    });
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "nas",
      address: "http://192.168.1.20:34567",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
  });

  it("never sends a request where another server answers", async () => {
    section(mapNode({ issues: ["wrongServer"] }));
    fireEvent.click(screen.getByTestId("data-sync-start-toggle"));
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    await act(() => confirmation().action() as Promise<void>);

    expect(vi.mocked(dataSyncApi.createLink).mock.calls[0][0]).not.toHaveProperty("address");
  });

  it("offers nothing for a device it cannot name by its install id", () => {
    const { container } = section(mapNode({ id: "manager:d1", keys: ["device:d1"] }));

    expect(container).toBeEmptyDOMElement();
  });

  it("opens a code for the device from the editor", async () => {
    section(
      mapNode({
        sources: {
          sync: mapPeer("nas", "NAS", { peerMayReadUs: false }),
          syncRequests: [],
        },
      }),
    );

    fireEvent.click(screen.getByText("dataSync.invitation.createFor NAS"));
    expect(await screen.findByTestId("data-sync-invitation")).toHaveTextContent(
      "dataSync.invitation.createFor NAS",
    );
  });
});

describe("data sync for a device found nearby", () => {
  const ghost = (sharesDefinitions?: boolean) =>
    render(
      <MemoryRouter>
        <DataSyncGhostAction
          actions={recorded.actions}
          node={{
            name: "Garage",
            sources: {
              sharingCandidate: {
                nodeId: "garage",
                address: "http://192.168.1.70:34567",
                sharesDefinitions,
              },
            },
          }}
          now={NOW}
          selfName="Studio PC"
          view={mapView()}
        />
      </MemoryRouter>,
    );

  it("offers to sync with one that shares its definitions", async () => {
    ghost(true);

    expect(screen.getByText("dataSync.map.ghost.shares")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("data-sync-start-toggle"));
    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(confirmation().title).toBe("dataSync.twoWay.title Garage");
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.createLink).toHaveBeenCalledWith(
      expect.objectContaining({
        peerNodeId: "garage",
        address: "http://192.168.1.70:34567",
        mode: DataSyncLinkMode.TwoWay,
      }),
    );
  });

  it.each([[false], [undefined]])(
    "tells how to share, never hiding the way, when it says %s",
    (shares) => {
      ghost(shares);

      expect(screen.getByTestId("data-sync-ghost-guidance")).toHaveTextContent(
        "dataSync.map.ghost.notSharing",
      );
      expect(screen.queryByTestId("data-sync-start-toggle")).toBeNull();
    },
  );
});

describe("this device's data sync, in the device map's details", () => {
  // Its first read of this device's side settles inside the render.
  const self = (view = mapView()) =>
    act(async () => {
      render(
        <MemoryRouter>
          <DataSyncSelfSection actions={recorded.actions} now={NOW} view={view} />
        </MemoryRouter>,
      );
    });

  it("counts the devices it receives from and those that read it, and links to the page", async () => {
    await self(
      mapView({
        peers: [
          mapPeer("nas", "NAS"),
          mapPeer("lap", "Laptop", { mode: DataSyncLinkMode.Follow, peerMayReadUs: false }),
          mapPeer("pc", "PC", null),
        ],
      }),
    );
    const section = screen.getByTestId("data-sync-self-section");

    expect(
      within(section).getByText("dataSync.map.self.receivesFrom").nextSibling,
    ).toHaveTextContent("2");
    expect(within(section).getByText("dataSync.map.self.readBy").nextSibling).toHaveTextContent(
      "2",
    );
    expect(within(section).getByText("dataSync.link.openPage").closest("a")).toHaveAttribute(
      "href",
      "/data-sync",
    );
  });

  it("offers a code that turns on what it needs first, never a disabled button", async () => {
    await self(mapView({ sharingEnabled: false, remoteAccessMode: RemoteAccessMode.Disabled }));

    expect(screen.getByTestId("data-sync-self-code")).not.toBeDisabled();
    fireEvent.click(screen.getByTestId("data-sync-self-code"));
    expect(screen.queryByTestId("data-sync-invitation-create")).toBeNull();
    expect(confirmation()).toMatchObject({
      title: "dataSync.sharing.onTitle",
      description: "dataSync.invitation.turnOnFirst",
      warning: "dataSync.twoWay.turnsOnSharing dataSync.sharing.remoteAccess",
    });
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    // Then the code dialog opens.
    expect(screen.getByTestId("data-sync-invitation-create")).toBeInTheDocument();
  });

  it("shows the page's sharing controls, turning sharing off from any window", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview({ canManageSharing: false }));
    await self();

    expect(screen.queryByTestId("data-sync-sharing-switch")).toBeNull();
    expect(screen.queryByTestId("data-sync-self-code")).toBeNull();
    fireEvent.click(screen.getByTestId("data-sync-sharing-off"));
    expect(confirmation().title).toBe("dataSync.sharing.offTitle");
    expect(dataSyncApi.setSharing).not.toHaveBeenCalled();
  });

  it("reads this device's own side once, for its state", async () => {
    await self();

    expect(dataSyncApi.overview).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("data-sync-self-status")).toHaveTextContent("dataSync.status.InStep");
  });
});
