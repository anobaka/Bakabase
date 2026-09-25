import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import HistoryDrawing from "../components/HistoryDrawing";
import HistoryList from "../components/HistoryList";
import { dataSyncApi } from "../api";
import { forgetBackupFolder } from "../hooks/useBackupTarget";

import { historyCountsOf, historyEntry, link, minutesAgo, NOW } from "./dataSyncFixtures";

import {
  DataSyncHistoryKind,
  DataSyncItemAction,
  DataSyncItemOutcome,
  DataSyncLinkMode,
  DataSyncPlanItemType,
  DataSyncUndoAction,
  DataSyncUndoBlock,
  DataSyncUndoState,
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
    undoPreview: vi.fn(),
    undo: vi.fn(),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: {
      getAppInfo: vi.fn(async () => ({ code: 0, data: { backupPath: "/data/backups" } })),
    },
  },
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
  historyEntry(5, K.Undo, { undoState: DataSyncUndoState.Expired }),
  historyEntry(6, K.Restore, { undoState: DataSyncUndoState.Undone, undoneAt: minutesAgo(30) }),
  historyEntry(7, K.EntitySetting, { undoState: DataSyncUndoState.Expired }),
];

const entry = (id: number) =>
  document.querySelector<HTMLElement>(
    `[data-testid="data-sync-history-entry"][data-entry="${id}"]`,
  )!;

const renderList = (links = [link(1, "node-nas", "NAS", { mode: DataSyncLinkMode.Follow })]) =>
  render(
    <HistoryList links={links} now={NOW} selfName="This PC" version={0} onChanged={vi.fn()} />,
  );

beforeEach(() => {
  vi.clearAllMocks();
  forgetBackupFolder();
  vi.mocked(dataSyncApi.history).mockResolvedValue(entries());
});
afterEach(cleanup);

describe("the history", () => {
  it("names each kind, the device, what it did, and whether it can be undone", async () => {
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
    // Every kind but an undo can be undone, while it can.
    expect(within(entry(3)).getByTestId("data-sync-history-undo")).toBeInTheDocument();
    expect(within(entry(4)).getByTestId("data-sync-history-undo")).toBeInTheDocument();
    expect(within(entry(5)).queryByTestId("data-sync-history-undo")).toBeNull();
    expect(within(entry(6)).getByTestId("data-sync-history-undone")).toBeInTheDocument();
    expect(entry(7)).toHaveTextContent("dataSync.history.expired");
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

  it("previews an undo in groups, says what it cannot bring back, and starts it", async () => {
    vi.mocked(dataSyncApi.undoPreview).mockResolvedValue({
      canUndo: true,
      items: [
        {
          kind: "customProperty",
          localKey: "13",
          name: "Rating",
          action: DataSyncUndoAction.Remove,
          valueCount: 0,
          settingsMayReferenceIt: true,
          recreatedGetsNewId: false,
        },
        {
          kind: "customProperty",
          localKey: "12",
          name: "Genre",
          action: DataSyncUndoAction.Revert,
          blocked: DataSyncUndoBlock.ChangedSinceImport,
          valueCount: 412,
          settingsMayReferenceIt: false,
          recreatedGetsNewId: false,
        },
        {
          kind: "customProperty",
          localKey: "14",
          name: "Mood",
          action: DataSyncUndoAction.Recreate,
          settingsMayReferenceIt: false,
          recreatedGetsNewId: true,
        },
        {
          kind: "customProperty",
          localKey: "15",
          name: "Studio",
          action: DataSyncUndoAction.Revert,
          settingsMayReferenceIt: false,
          recreatedGetsNewId: false,
        },
      ],
    });
    vi.mocked(dataSyncApi.undo).mockResolvedValue({ taskId: "DataSyncUndo:3" });
    renderList();
    await waitFor(() => expect(entry(3)).toBeTruthy());
    fireEvent.click(within(entry(3)).getByTestId("data-sync-history-undo"));
    const dialog = await screen.findByTestId("data-sync-undo");

    await waitFor(() =>
      expect(
        within(dialog)
          .getAllByTestId("data-sync-undo-group")
          .map((group) => group.getAttribute("data-group")),
      ).toEqual(["remove", "revert", "recreate", "keep"]),
    );
    const group = (name: string) => dialog.querySelector<HTMLElement>(`[data-group="${name}"]`)!;

    expect(group("remove")).toHaveTextContent("dataSync.undo.note.remove NAS");
    expect(group("remove")).toHaveTextContent("dataSync.undo.settingsLoseIt");
    // A link that only receives: the other device may send the change again.
    expect(group("revert")).toHaveTextContent("dataSync.undo.note.revertFollow NAS");
    expect(group("recreate")).toHaveTextContent("dataSync.undo.note.recreate");
    expect(group("recreate")).toHaveTextContent("dataSync.undo.newId");
    expect(group("keep")).toHaveTextContent("Genre");
    expect(group("keep")).toHaveTextContent("dataSync.undo.blocked.ChangedSinceImport");
    await waitFor(() =>
      expect(within(dialog).getByTestId("data-sync-undo-backup")).toHaveTextContent(
        "dataSync.undo.backup /data/backups",
      ),
    );
    expect(dialog).toHaveTextContent("dataSync.undo.retention");

    await act(async () => {
      fireEvent.click(within(dialog).getByTestId("data-sync-undo-confirm"));
    });
    expect(dataSyncApi.undo).toHaveBeenCalledWith(3);
    expect(screen.queryByTestId("data-sync-undo")).toBeNull();
    expect(entry(3)).toHaveTextContent("dataSync.history.undoing");
  });

  it("does not offer Undo where the preview says it cannot", async () => {
    vi.mocked(dataSyncApi.undoPreview).mockResolvedValue({
      canUndo: false,
      items: [],
      problem: { code: 9 as never },
    });
    renderList();
    await waitFor(() => expect(entry(3)).toBeTruthy());
    fireEvent.click(within(entry(3)).getByTestId("data-sync-history-undo"));
    const dialog = await screen.findByTestId("data-sync-undo");

    await waitFor(() => expect(dialog).toHaveTextContent("dataSync.problem.UndoNotAvailable"));
    expect(within(dialog).getByTestId("data-sync-undo-confirm")).toBeDisabled();
  });
});

describe("the history drawing", () => {
  it("draws who sent definitions here, three at most, then says how many more", () => {
    const from = (id: number, nodeId: string, name: string) =>
      historyEntry(id, K.AutoSync, {
        peerNodeId: nodeId,
        peerName: name,
        appliedAt: minutesAgo(id),
      });

    render(
      <HistoryDrawing
        entries={[
          from(1, "a", "PC-1"),
          from(2, "b", "PC-2"),
          from(3, "c", "NAS"),
          from(4, "d", "Laptop"),
        ]}
        now={NOW}
        selfName="This PC"
      />,
    );

    const drawing = screen.getByTestId("data-sync-history-drawing");

    expect(drawing.querySelectorAll("[data-source]")).toHaveLength(3);
    expect(drawing).toHaveTextContent("dataSync.history.drawing.more 1");
  });

  it("shows this device alone when nothing was received", () => {
    render(<HistoryDrawing entries={[]} now={NOW} selfName="This PC" />);

    expect(screen.getByTestId("data-sync-history-drawing")).toHaveTextContent(
      "dataSync.history.drawing.empty",
    );
  });
});
