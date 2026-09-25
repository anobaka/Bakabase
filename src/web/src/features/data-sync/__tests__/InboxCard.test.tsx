import type * as Api from "../api";
import type { DataSyncInboxItemView } from "../api";
import type { InboxCardProps } from "../components/InboxCard";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import InboxCard from "../components/InboxCard";
import InboxList from "../components/InboxList";
import { dataSyncApi } from "../api";
import { forgetBackupFolder } from "../hooks/useBackupTarget";
import { groupInbox } from "../inboxModels";
import { useDataSyncStore } from "../stores/dataSync";
import { syncPeerFromLink } from "../viewModels";

import {
  inboxItem,
  inboxPayload,
  link,
  minutesAgo,
  nameConflict,
  NOW,
  overview,
} from "./dataSyncFixtures";

import {
  ClientMode,
  DataSyncFieldResolution,
  DataSyncInboxAction,
  DataSyncInboxClosure,
  DataSyncInboxItemOrigin,
  DataSyncInboxItemType,
  DataSyncNaturalMatch,
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
    previewInboxItem: vi.fn(),
    inbox: vi.fn(),
    resolve: vi.fn(),
    pauseLink: vi.fn(async () => ({})),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: {
      getAppInfo: vi.fn(async () => ({ code: 0, data: { backupPath: "/data/backups" } })),
    },
  },
}));
vi.mock("@/features/federation/serverApi", () => ({
  managedServerApi: { list: vi.fn(async () => ({ available: true, servers: [], requests: [] })) },
}));
vi.mock("@/features/federation/switching", () => ({
  openManagedServer: vi.fn(async () => undefined),
  openConsoleTarget: vi.fn(async () => undefined),
}));

const A = DataSyncInboxAction;
const T = DataSyncInboxItemType;
const backup = { size: "50 MB", folder: "/data/backups" };
const initialRemote = useRemoteAccessStore.getState();

const renderCard = (items: DataSyncInboxItemView[], props: Partial<InboxCardProps> = {}) => {
  const onResolve = vi.fn();
  const onPauseLink = vi.fn();
  const [card] = groupInbox(items);

  render(
    <InboxCard
      backup={backup}
      busy={false}
      card={card}
      onPauseLink={onPauseLink}
      onResolve={onResolve}
      {...props}
    />,
  );

  return { onResolve, onPauseLink };
};

const action = (name: string) =>
  document.querySelector<HTMLButtonElement>(
    `[data-testid="data-sync-inbox-action"][data-action="${name}"]`,
  )!;
const actionNames = () =>
  screen
    .getAllByTestId("data-sync-inbox-action")
    .map((button) => button.getAttribute("data-action"));
const radio = (label: string) =>
  screen.getByText(label, { selector: "label" }).querySelector("input")!;

beforeEach(() => {
  vi.clearAllMocks();
  forgetBackupFolder();
  useDataSyncStore.getState().clear();
  useDataSyncStore.getState().setOverview(overview());
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Disabled,
  });
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("a conflict card", () => {
  it("sets the last agreed value faint above both sides, and takes the other device's", () => {
    const { onResolve } = renderCard([nameConflict(1)]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.FieldConflictName 作者",
    );
    const comparison = screen.getByTestId("data-sync-inbox-comparison");

    expect(comparison).toHaveTextContent("dataSync.inbox.card.lastAgreed");
    expect(
      within(comparison)
        .getAllByTestId("data-sync-value")
        .map((v) => v.textContent),
    ).toEqual(["Artist", "作者", "Artists"]);
    expect(within(comparison).getAllByTestId("data-sync-value")[0].className).toContain(
      "opacity-60",
    );
    const apply = screen.getByTestId("data-sync-inbox-apply");

    expect(apply).toBeDisabled();
    fireEvent.click(radio("dataSync.inbox.action.UseRemote NAS"));
    fireEvent.click(apply);
    expect(onResolve).toHaveBeenCalledWith({
      items: [{ itemId: 1, action: A.UseRemote, token: "token-1", customValue: undefined }],
      backupBeforeDestructive: true,
    });
  });

  it("takes a name of the reader's own", () => {
    const { onResolve } = renderCard([nameConflict(1)]);

    fireEvent.click(radio("dataSync.inbox.action.UseCustom"));
    const apply = screen.getByTestId("data-sync-inbox-apply");

    expect(apply).toBeDisabled();
    fireEvent.change(screen.getByTestId("data-sync-inbox-custom"), {
      target: { value: "  Creators " },
    });
    fireEvent.click(apply);
    expect(onResolve.mock.calls[0][0].items).toEqual([
      { itemId: 1, action: A.UseCustom, token: "token-1", customValue: "Creators" },
    ]);
  });

  it("decides every device's conflict of the definition at once", () => {
    const { onResolve } = renderCard([
      nameConflict(1),
      nameConflict(2, { peerNodeId: "node-laptop", peerName: "Laptop" }, "Author"),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.FieldConflict 作者 NAS NAS, Laptop",
    );
    fireEvent.click(radio("dataSync.inbox.action.UseRemote Laptop"));
    fireEvent.click(screen.getByTestId("data-sync-inbox-apply"));
    expect(
      onResolve.mock.calls[0][0].items.map((input: { itemId: number; action: number }) => [
        input.itemId,
        input.action,
      ]),
    ).toEqual([
      [1, A.KeepLocal],
      [2, A.UseRemote],
    ]);
    fireEvent.click(action("Detach"));
    expect(
      onResolve.mock.calls[1][0].items.every(
        (input: { action: number }) => input.action === A.Detach,
      ),
    ).toBe(true);
  });

  it("names the option a rename conflict is about, and how many use it", () => {
    renderCard([
      inboxItem(2, T.ChildRenameConflict, [A.KeepLocal, A.UseRemote, A.UseCustom, A.Detach], {
        subjectPath: "choice:c-horror",
        payload: inboxPayload({
          usageCount: 30,
          fields: [
            {
              path: "choice:c-horror",
              resolution: DataSyncFieldResolution.Conflict,
              base: { text: "Horror" },
              local: { text: "Horror films" },
              remote: { text: "恐怖" },
            },
          ],
        }),
      }),
    ]);

    expect(screen.getByTestId("data-sync-inbox-field")).toHaveTextContent(
      "dataSync.inbox.card.option Horror",
    );
    expect(screen.getByTestId("data-sync-inbox-field")).toHaveTextContent(
      "dataSync.inbox.card.inUse 30",
    );
  });
});

describe("cards of one item", () => {
  it("loads a type change's preview when it is expanded, and asks before converting", async () => {
    vi.mocked(dataSyncApi.previewInboxItem).mockResolvedValue({
      fromSubtype: "MultipleChoice",
      toSubtype: "SingleChoice",
      valueCount: 1234,
      changedCount: 1234,
      lossyCount: 210,
      samples: [{ from: "Action, Comedy", to: "Action" }],
    });
    const { onResolve } = renderCard([
      inboxItem(3, T.TypeChange, [A.Convert, A.Detach, A.KeepLocal], {
        subjectPath: "type",
        payload: inboxPayload({
          valueCount: 1234,
          localSubtype: "MultipleChoice",
          remoteSubtype: "SingleChoice",
        }),
      }),
    ]);

    expect(dataSyncApi.previewInboxItem).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-inbox-preview-toggle"));
    });
    expect(dataSyncApi.previewInboxItem).toHaveBeenCalledWith(3);
    expect(screen.getByTestId("data-sync-inbox-preview")).toHaveTextContent(
      "dataSync.inbox.card.previewLossy 1234 1234 210",
    );
    expect(screen.getByTestId("data-sync-inbox-preview")).toHaveTextContent(
      "Action, Comedy → Action",
    );
    expect(actionNames()).toEqual(["Convert", "Detach", "KeepLocalType"]);
    // Lossy: backed up first by default, and asked.
    expect(screen.getByTestId("data-sync-backup")).toBeChecked();
    fireEvent.click(action("Convert"));
    const [batch, confirmation] = onResolve.mock.calls[0];

    expect(batch).toEqual({
      items: [expect.objectContaining({ itemId: 3, action: A.Convert, token: "token-3" })],
      backupBeforeDestructive: true,
    });
    expect(confirmation.warning).toBe("dataSync.inbox.confirm.backedUp");
  });

  it("deletes a definition with values only after asking, backed up unless unticked", () => {
    const { onResolve } = renderCard([
      inboxItem(4, T.DeletedThere, [A.DeleteHere, A.KeepHereOnly, A.RestoreEverywhere], {
        payload: inboxPayload({ entityName: "Mood", valueCount: 412 }),
      }),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.DeletedThere Mood NAS",
    );
    expect(screen.getByText("dataSync.inbox.card.valuesHere 412")).toBeInTheDocument();
    expect(actionNames()).toEqual(["DeleteHere", "KeepHereOnly", "RestoreEverywhere"]);
    const box = screen.getByTestId("data-sync-backup");

    expect(box).toBeChecked();
    expect(box.parentElement).toHaveTextContent("dataSync.inbox.backup 50 MB /data/backups");
    fireEvent.click(box);
    fireEvent.click(action("DeleteHere"));
    const [batch, confirmation] = onResolve.mock.calls[0];

    expect(batch.backupBeforeDestructive).toBe(false);
    expect(confirmation).toMatchObject({
      title: "dataSync.inbox.action.DeleteHere NAS",
      description: "dataSync.inbox.confirm.delete Mood NAS NAS 412",
      warning: "dataSync.inbox.confirm.notBackedUp",
    });
    fireEvent.click(action("KeepHereOnly"));
    expect(onResolve.mock.calls[1]).toEqual([
      {
        items: [expect.objectContaining({ itemId: 4, action: A.KeepHereOnly })],
        backupBeforeDestructive: false,
      },
      undefined,
    ]);
  });

  it("shows the option removed there that is used here", () => {
    renderCard([
      inboxItem(9, T.ChildDeletedInUse, [A.DeleteHere, A.KeepHereOnly, A.RestoreEverywhere], {
        origin: DataSyncInboxItemOrigin.State,
        subjectPath: "choice:c-horror",
        payload: inboxPayload({
          usageCount: 30,
          children: [{ text: "Horror", color: "#aa0000" }],
          childrenTotal: 1,
        }),
      }),
    ]);

    expect(screen.getByTestId("data-sync-inbox-children")).toHaveTextContent("Horror");
    expect(screen.getByText("dataSync.inbox.card.usedHere 30")).toBeInTheDocument();
    expect(actionNames()).toEqual([
      "DeleteHereChild",
      "KeepHereOnlyChild",
      "RestoreEverywhereChild",
    ]);
  });

  it("restores a definition deleted here, empty, or keeps it deleted", () => {
    renderCard([
      inboxItem(5, T.DeletedHereEditedThere, [A.RestoreHere, A.KeepDeleted], {
        localKey: undefined,
      }),
    ]);

    expect(screen.getByText("dataSync.inbox.card.restoreEmpty")).toBeInTheDocument();
    expect(actionNames()).toEqual(["RestoreHere", "KeepDeleted"]);
    expect(screen.queryByTestId("data-sync-backup")).toBeNull();
  });

  it("suggests a link with its match level, keeps both under a name, or skips", () => {
    const { onResolve } = renderCard([
      inboxItem(6, T.LinkSuggestion, [A.Link, A.KeepBoth, A.Skip], {
        localKey: undefined,
        payload: inboxPayload({
          entityName: "Rating",
          candidates: [
            { localKey: "15", name: "Rating", match: DataSyncNaturalMatch.Exact, updatable: true },
            { localKey: "16", name: "rating", match: DataSyncNaturalMatch.Clash, updatable: false },
          ],
        }),
      }),
    ]);

    const candidates = screen.getByTestId("data-sync-inbox-candidates");

    expect(candidates).toHaveTextContent("dataSync.inbox.match.Exact");
    expect(candidates).toHaveTextContent("dataSync.inbox.match.otherType");
    expect(action("Link")).toHaveTextContent("dataSync.inbox.action.Link NAS Rating");
    fireEvent.click(action("KeepBoth"));
    const name = within(screen.getByTestId("data-sync-inbox-input")).getByRole("textbox");

    expect(name).toHaveValue("dataSync.plan.separateName Rating NAS");
    fireEvent.change(name, { target: { value: "Rating (NAS)" } });
    fireEvent.click(screen.getByTestId("data-sync-inbox-input-apply"));
    expect(onResolve.mock.calls[0][0].items).toEqual([
      expect.objectContaining({ itemId: 6, action: A.KeepBoth, newName: "Rating (NAS)" }),
    ]);
  });

  it("keeps one of two definitions here linked to the other device's record (row I)", () => {
    const { onResolve } = renderCard([
      inboxItem(7, T.IdentityConflict, [A.KeepWithEntity, A.Detach], {
        payload: inboxPayload({
          entityName: "Artist",
          candidates: [
            { localKey: "17", name: "Artist", match: DataSyncNaturalMatch.Exact, updatable: true },
            { localKey: "18", name: "Author", match: DataSyncNaturalMatch.None, updatable: true },
          ],
        }),
      }),
    ]);

    expect(actionNames()).toEqual(["KeepWithEntity", "KeepWithEntity", "Detach"]);
    fireEvent.click(screen.getByText("dataSync.inbox.action.KeepWithEntity NAS Author"));
    expect(onResolve.mock.calls[0][0].items[0]).toMatchObject({
      action: A.KeepWithEntity,
      targetLocalKey: "18",
    });
  });

  it("lists the other device's records that bind to one definition here (row M)", () => {
    const { onResolve } = renderCard([
      inboxItem(8, T.IdentityConflict, [A.KeepRecordLinked, A.Detach], {
        payload: inboxPayload({
          entityName: "Artist",
          records: [
            { primaryKey: "k-artist", name: "Artist" },
            { primaryKey: "k-author", name: "Author" },
          ],
        }),
      }),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.IdentityConflictRecords Artist NAS",
    );
    expect(screen.getByTestId("data-sync-inbox-records")).toHaveTextContent(
      "dataSync.inbox.card.record NAS Artist",
    );
    expect(screen.getByTestId("data-sync-inbox-records")).toHaveTextContent(
      "dataSync.inbox.card.record NAS Author",
    );
    fireEvent.click(screen.getByText("dataSync.inbox.action.KeepRecordLinked NAS Author"));
    expect(onResolve.mock.calls[0][0].items[0]).toMatchObject({
      action: A.KeepRecordLinked,
      targetRecordKey: "k-author",
    });
  });

  it("shows the first options of a mass deletion, and how many there are", () => {
    renderCard([
      inboxItem(10, T.MassChildDeletion, [A.ReviewEach, A.ApplyAll, A.RestoreEverywhere], {
        origin: DataSyncInboxItemOrigin.State,
        payload: inboxPayload({
          subtype: "Tags",
          children: Array.from({ length: 50 }, (_, index) => ({
            text: `Tag ${index}`,
            group: "G",
          })),
          childrenTotal: 180,
        }),
      }),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.MassChildDeletion Genre NAS NAS 180",
    );
    expect(
      within(screen.getByTestId("data-sync-inbox-children")).getAllByTestId("data-sync-value"),
    ).toHaveLength(50);
    expect(screen.getByText("dataSync.inbox.card.firstOf 50 180")).toBeInTheDocument();
    expect(actionNames()).toEqual(["ReviewEach", "ApplyAll", "RestoreEverywhereAll"]);
  });

  it("asks about a change that undid what sync applied", () => {
    renderCard([
      inboxItem(11, T.SuspectedLostUpdate, [A.Publish, A.Reapply], {
        linkId: undefined,
        peerNodeId: undefined,
        peerName: undefined,
        origin: DataSyncInboxItemOrigin.State,
        payload: inboxPayload({
          peerName: undefined,
          remoteEditor: undefined,
          fields: [
            {
              path: "choice:c-horror",
              resolution: DataSyncFieldResolution.Unchanged,
              base: { text: "Horror films" },
              local: { text: "Horror" },
            },
          ],
        }),
      }),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent(
      "dataSync.inbox.type.SuspectedLostUpdate Genre",
    );
    expect(screen.getByText("dataSync.inbox.card.lostUpdateHint")).toBeInTheDocument();
    expect(actionNames()).toEqual(["Publish", "Reapply"]);
  });

  it("lists a large change, applies it all, or pauses the link", () => {
    const { onPauseLink } = renderCard([
      inboxItem(12, T.LargeChange, [A.ApplyAll], {
        kind: "",
        localKey: undefined,
        subjectPath: "largeChange",
        origin: DataSyncInboxItemOrigin.State,
        payload: inboxPayload({
          entityName: "",
          childrenTotal: 182,
          largeChange: [
            { name: "Genre", kind: "customProperty", create: false, changes: 40 },
            { name: "Studio", kind: "customProperty", create: true, changes: 1 },
          ],
        }),
      }),
    ]);

    expect(screen.getByRole("heading")).toHaveTextContent("dataSync.inbox.type.LargeChange");
    expect(screen.getByTestId("data-sync-inbox-large")).toHaveTextContent(
      "Genre · dataSync.inbox.card.changes 40",
    );
    expect(screen.getByTestId("data-sync-inbox-large")).toHaveTextContent(
      "Studio · dataSync.inbox.card.newDefinition",
    );
    expect(actionNames()).toEqual(["ApplyAllLarge", "Pause"]);
    fireEvent.click(action("Pause"));
    expect(onPauseLink).toHaveBeenCalledWith(1);
  });

  it("says where a card was resolved when it closed elsewhere", () => {
    const item = nameConflict(13);

    renderCard([item], {
      closedItem: {
        ...item,
        closedAt: minutesAgo(1),
        closure: DataSyncInboxClosure.ResolvedElsewhere,
        closedByName: "Laptop",
      },
    });

    expect(screen.getByTestId("data-sync-inbox-closed")).toHaveTextContent(
      "dataSync.inbox.closure.resolvedOn Laptop",
    );
    expect(screen.queryByTestId("data-sync-inbox-apply")).toBeNull();
  });
});

describe("the list of what needs you", () => {
  const deletion = (id: number) =>
    inboxItem(id, T.DeletedThere, [A.DeleteHere, A.KeepHereOnly], {
      localKey: `d${id}`,
      payload: inboxPayload({ entityName: `Mood ${id}`, valueCount: 3 }),
    });
  const closedElsewhere = {
    ...nameConflict(13, { localKey: "19" }),
    closedAt: minutesAgo(30),
    closure: DataSyncInboxClosure.ResolvedElsewhere,
    closedByName: "Laptop",
  };

  const renderList = (props: Partial<Parameters<typeof InboxList>[0]> = {}) =>
    render(
      <MemoryRouter>
        <InboxList focus={false} now={NOW} peers={[]} version={0} onChanged={vi.fn()} {...props} />
      </MemoryRouter>,
    );

  beforeEach(() => {
    vi.mocked(dataSyncApi.inbox).mockImplementation(async (query) =>
      query?.openOnly
        ? { items: [deletion(30), deletion(31), nameConflict(1)], total: 3, openTotal: 3 }
        : {
            items: [deletion(30), deletion(31), nameConflict(1), closedElsewhere],
            total: 4,
            openTotal: 3,
          },
    );
    vi.mocked(dataSyncApi.resolve).mockResolvedValue({ taskId: "DataSyncResolve:batch-1" });
  });

  it("deletes all at once, asking once, with one backup", async () => {
    renderList();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-inbox-card")).toHaveLength(3));

    const bulk = within(screen.getByTestId("data-sync-inbox-bulk"));

    expect(bulk.getByTestId("data-sync-backup")).toBeChecked();
    await waitFor(() =>
      expect(bulk.getByTestId("data-sync-backup").parentElement).toHaveTextContent(
        "dataSync.inbox.backup 50 MB /data/backups",
      ),
    );
    fireEvent.click(document.querySelector<HTMLElement>('[data-bulk="deleteAll"]')!);
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("dataSync.inbox.confirm.deleteAll 2");
    expect(dialog).toHaveTextContent("dataSync.inbox.confirm.backedUp");
    await act(async () => {
      fireEvent.click(within(dialog).getByText("federation.confirm"));
    });
    expect(dataSyncApi.resolve).toHaveBeenCalledWith({
      items: [
        { itemId: 30, action: A.DeleteHere, token: "token-30" },
        { itemId: 31, action: A.DeleteHere, token: "token-31" },
      ],
      backupBeforeDestructive: true,
    });
  });

  it("keeps what was decided in the last week under Recently resolved", async () => {
    renderList();
    await waitFor(() => expect(screen.getByTestId("data-sync-inbox-recent")).toBeInTheDocument());

    expect(screen.getByTestId("data-sync-inbox-recent")).toHaveTextContent(
      "dataSync.inbox.recent 1",
    );
    expect(screen.getByTestId("data-sync-inbox-recent")).toHaveTextContent(
      "dataSync.inbox.closure.resolvedOn Laptop",
    );
  });

  it("lets a card resolved on another device say so as it leaves", async () => {
    const view = (version: number) => (
      <MemoryRouter>
        <InboxList focus={false} now={NOW} peers={[]} version={version} onChanged={vi.fn()} />
      </MemoryRouter>
    );
    const page = render(view(0));

    await waitFor(() => expect(screen.getAllByTestId("data-sync-inbox-card")).toHaveLength(3));
    // Read again: the conflict was decided on the laptop meanwhile.
    vi.mocked(dataSyncApi.inbox).mockImplementation(async (query) =>
      query?.openOnly
        ? { items: [deletion(30), deletion(31)], total: 2, openTotal: 2 }
        : {
            items: [
              deletion(30),
              deletion(31),
              {
                ...nameConflict(1),
                closedAt: minutesAgo(0),
                closure: DataSyncInboxClosure.ResolvedElsewhere,
                closedByName: "Laptop",
              },
            ],
            total: 3,
            openTotal: 2,
          },
    );
    page.rerender(view(1));

    await waitFor(() => expect(screen.getByTestId("data-sync-inbox-leaving")).toBeInTheDocument());
    expect(
      within(screen.getByTestId("data-sync-inbox-leaving")).getByTestId("data-sync-inbox-closed"),
    ).toHaveTextContent("dataSync.inbox.closure.resolvedOn Laptop");
    expect(
      within(screen.getByTestId("data-sync-inbox-leaving")).queryByTestId("data-sync-inbox-apply"),
    ).toBeNull();
  });

  it("says where a resolution failed, on its card", async () => {
    vi.mocked(dataSyncApi.resolve).mockResolvedValue({
      problem: { code: 27 as never },
    });
    renderList();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-inbox-card")).toHaveLength(3));
    const card = document.querySelector<HTMLElement>('[data-card="item:31"]')!;

    await act(async () => {
      fireEvent.click(
        within(card).getByText("dataSync.inbox.action.KeepHereOnly NAS", { selector: "button" }),
      );
    });
    expect(within(card).getByTestId("data-sync-error")).toHaveTextContent(
      "dataSync.problem.ResolveTogether",
    );
  });

  it("shows only the device asked for, and says where decisions wait on another device", async () => {
    const hub = syncPeerFromLink(
      link(1, "node-nas", "NAS", {
        peerAttention: {
          headless: true,
          openDecisions: 2,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
    );

    vi.mocked(dataSyncApi.inbox).mockResolvedValue({ items: [], total: 0, openTotal: 0 });
    renderList({ focus: true, initialPeer: "node-nas", peers: [hub] });
    await waitFor(() => expect(screen.getByTestId("data-sync-elsewhere")).toBeInTheDocument());

    expect(screen.getByTestId("data-sync-elsewhere")).toHaveTextContent(
      "dataSync.status.NeedsYouThere NAS 2",
    );
    // This device does not manage the NAS: the line says how to get there.
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-elsewhere")).toHaveTextContent(
        "dataSync.inbox.elsewhere.howTo NAS",
      ),
    );
    expect(screen.getByTestId("data-sync-inbox-filter-device")).toHaveValue("node-nas");
    expect(screen.getByTestId("data-sync-inbox-empty")).toBeInTheDocument();
  });
});
