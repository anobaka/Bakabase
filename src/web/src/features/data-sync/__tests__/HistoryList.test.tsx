import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import HistoryList from "../components/HistoryList";
import { dataSyncApi } from "../api";
import { syncPeerOf } from "../viewModels";

import { historyCountsOf, historyEntry, mapPeer, minutesAgo, NOW } from "./dataSyncFixtures";

import {
  DataSyncHistoryKind,
  DataSyncItemAction,
  DataSyncItemOutcome,
  DataSyncLinkMode,
  DataSyncPlanItemType,
} from "@/sdk/constants";

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
    history: vi.fn(),
    historyEntry: vi.fn(),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: {
      getAppInfo: vi.fn(async () => ({ code: 0, data: { backupPath: "/data/backups" } })),
    },
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));

const K = DataSyncHistoryKind;
const entries = () => [
  historyEntry(1, K.FirstLink, { counts: historyCountsOf({ created: 94, linked: 6 }) }),
  historyEntry(2, K.CopyOnce),
  historyEntry(3, K.AutoSync, { counts: historyCountsOf({ updated: 2, deleted: 1 }) }),
  historyEntry(4, K.Resolution, {
    peerNodeId: undefined,
    peerName: undefined,
    linkId: undefined,
    counts: historyCountsOf({ resolved: 2 }),
  }),
  historyEntry(5, K.Undo),
  historyEntry(6, K.Restore, { undoneAt: minutesAgo(30) }),
  historyEntry(7, K.EntitySetting),
];

const entry = (id: number) =>
  document.querySelector<HTMLElement>(
    `[data-testid="data-sync-history-entry"][data-entry="${id}"]`,
  )!;

const renderList = (
  peers = [syncPeerOf(mapPeer("node-nas", "NAS", { mode: DataSyncLinkMode.Follow }))],
) => render(<HistoryList now={NOW} peers={peers} version={0} />);

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(dataSyncApi.history).mockResolvedValue(entries());
});
afterEach(cleanup);

describe("the history", () => {
  it("replaces a bounded page and scopes the next request by cursor and device", async () => {
    const first = Array.from({ length: 50 }, (_, index) => historyEntry(100 - index, K.AutoSync));

    vi.mocked(dataSyncApi.history)
      .mockResolvedValueOnce(first)
      .mockResolvedValue([historyEntry(50, K.AutoSync)]);
    renderList();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-history-entry")).toHaveLength(50));
    fireEvent.click(screen.getByText("dataSync.activity.older"));
    await waitFor(() =>
      expect(dataSyncApi.history).toHaveBeenLastCalledWith({
        beforeId: 51,
        peerNodeId: undefined,
        take: 50,
      }),
    );
    await waitFor(() => expect(screen.getAllByTestId("data-sync-history-entry")).toHaveLength(1));
    expect(entry(100)).toBeNull();
    expect(entry(50)).toBeTruthy();
    fireEvent.click(screen.getByText("dataSync.activity.newest"));
    await waitFor(() =>
      expect(dataSyncApi.history).toHaveBeenLastCalledWith({
        beforeId: undefined,
        peerNodeId: undefined,
        take: 50,
      }),
    );
  });

  it("keeps the latest device filter when an earlier history request completes late", async () => {
    let resolveOld!: (value: Api.DataSyncHistoryEntry[]) => void;

    vi.mocked(dataSyncApi.history)
      .mockReturnValueOnce(
        new Promise((resolve) => {
          resolveOld = resolve;
        }),
      )
      .mockResolvedValue([
        historyEntry(99, K.AutoSync, { peerNodeId: "node-new", peerName: "New device" }),
      ]);
    const view = render(<HistoryList now={NOW} peer="node-old" version={0} />);

    await waitFor(() => expect(dataSyncApi.history).toHaveBeenCalledTimes(1));
    view.rerender(<HistoryList now={NOW} peer="node-new" version={0} />);
    await waitFor(() => expect(entry(99)).toBeTruthy());
    await act(async () => resolveOld([historyEntry(10, K.AutoSync, { peerNodeId: "node-old" })]));
    expect(entry(99)).toBeTruthy();
    expect(entry(10)).toBeNull();
  });

  it("keeps historical audit labels and details without offering any undo action", async () => {
    renderList();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-history-entry")).toHaveLength(7));

    expect(
      screen.getAllByTestId("data-sync-history-entry").map((row) => row.getAttribute("data-kind")),
    ).toEqual([
      "FirstLink",
      "CopyOnce",
      "AutoSync",
      "Resolution",
      "Undo",
      "Restore",
      "EntitySetting",
    ]);
    expect(entry(1)).toHaveTextContent("dataSync.history.kind.FirstLink");
    expect(entry(1)).toHaveTextContent("NAS");
    expect(within(entry(1)).getByTestId("data-sync-history-counts")).toHaveTextContent(
      "dataSync.history.count.created 94 · dataSync.history.count.linked 6",
    );
    expect(within(entry(3)).getByTestId("data-sync-history-counts")).toHaveTextContent(
      "dataSync.history.count.updated 2 · dataSync.history.count.deleted 1",
    );
    expect(within(entry(4)).getByTestId("data-sync-history-counts")).toHaveTextContent(
      "dataSync.history.count.resolved 2",
    );
    expect(screen.queryByTestId("data-sync-history-undo")).toBeNull();
    expect(screen.queryByTestId("data-sync-undo")).toBeNull();
    expect(within(entry(6)).getByTestId("data-sync-history-undone")).toBeInTheDocument();
    expect(screen.getByText("dataSync.history.auditOnly")).toBeInTheDocument();
    expect(screen.queryByText("dataSync.history.expired")).toBeNull();
  });

  it("shows what an entry did, item by item", async () => {
    vi.mocked(dataSyncApi.historyEntry).mockResolvedValue({
      entry: entries()[0],
      items: [
        {
          itemId: "customProperty/k/a",
          kind: "customProperty",
          name: "Genre",
          outcome: DataSyncItemOutcome.Applied,
          action: DataSyncItemAction.Updated,
          type: DataSyncPlanItemType.Update,
        },
        {
          itemId: "customProperty/k/b",
          kind: "customProperty",
          name: "Huge",
          outcome: DataSyncItemOutcome.Held,
          action: DataSyncItemAction.None,
          type: DataSyncPlanItemType.Held,
        },
      ],
    });
    renderList();
    await waitFor(() => expect(entry(1)).toBeTruthy());
    await act(async () => {
      fireEvent.click(within(entry(1)).getByTestId("data-sync-history-details"));
    });

    const items = within(entry(1)).getByTestId("data-sync-history-items");

    expect(dataSyncApi.historyEntry).toHaveBeenCalledWith(1);
    expect(items).toHaveTextContent("Genre");
    expect(items).toHaveTextContent("dataSync.history.action.Updated");
    expect(items).toHaveTextContent("dataSync.history.outcome.Held");
  });
});
