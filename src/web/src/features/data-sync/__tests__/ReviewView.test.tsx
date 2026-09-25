import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ReviewView from "../components/ReviewView";
import { dataSyncApi } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import {
  changeCounts,
  fieldChange,
  historyCountsOf,
  historyEntry,
  overview,
  planCandidate,
  planItem,
  plan as planOf,
  reviewResult,
} from "./dataSyncFixtures";

import {
  BTaskStatus,
  ClientMode,
  DataSyncDecisionErrorCode,
  DataSyncFieldChangeKind,
  DataSyncHeldReason,
  DataSyncHistoryKind,
  DataSyncItemAction,
  DataSyncItemOutcome,
  DataSyncLinkMode,
  DataSyncNaturalMatch,
  DataSyncPlanItemReason,
  DataSyncPlanItemType,
  DataSyncPlanResolution,
  DataSyncPlanItemTypeLabel,
  DataSyncProblemCode,
  DataSyncReviewState,
  DataSyncWarningCode,
  RemoteAccessMode,
} from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
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
    review: vi.fn(),
    refetchReview: vi.fn(),
    reviewChanges: vi.fn(),
    applyReview: vi.fn(),
    cancelReviewApply: vi.fn(),
    historyEntry: vi.fn(),
    updateLink: vi.fn(async () => ({})),
    forgetAccess: vi.fn(async () => undefined),
    setSharing: vi.fn(async () => undefined),
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));

const { Create, Update, Unchanged, Link, NeedsDecision, Held } = DataSyncPlanItemType;
const initialRemote = useRemoteAccessStore.getState();

/** The review of a laptop: a new property, an update with tags, a link to confirm, a decision. */
const laptopItems = () => [
  planItem("create", Create, "Studio"),
  planItem("update", Update, "Genre", {
    changes: [
      fieldChange(
        "name",
        DataSyncFieldChangeKind.Set,
        "name",
        { text: "Genres" },
        { from: { text: "Genre" } },
      ),
      fieldChange("tag:add:t1", DataSyncFieldChangeKind.AddChild, "tags", {
        text: "Isekai",
        group: "Genre",
      }),
    ],
  }),
  planItem("link", Link, "Rating", {
    candidates: [planCandidate("15", "Rating")],
    bulkLinkEligible: true,
  }),
  planItem("decide", NeedsDecision, "Artist", {
    reason: DataSyncPlanItemReason.AmbiguousNameMatch,
    candidates: [
      planCandidate("17", "Artist"),
      planCandidate("18", "artist", { match: DataSyncNaturalMatch.Similar }),
    ],
  }),
  planItem("held", Held, "Huge tags", { heldReason: DataSyncHeldReason.TooLarge }),
];

const renderReview = (props: Partial<Parameters<typeof ReviewView>[0]> = {}) => {
  const onClose = vi.fn();
  const onChanged = vi.fn();

  render(
    <MemoryRouter>
      <ReviewView
        peerName="Laptop"
        peerNodeId="node-laptop"
        reviewId="review-1"
        selfName="This PC"
        onChanged={onChanged}
        onClose={onClose}
        {...props}
      />
    </MemoryRouter>,
  );

  return { onClose, onChanged };
};

const row = (key: string) =>
  document.querySelector<HTMLElement>(`[data-item-id="customProperty/k/${key}"]`)!;
const applyButton = () => screen.getByTestId("data-sync-review-apply");
const loaded = () =>
  waitFor(() => expect(screen.getByTestId("data-sync-plan")).toBeInTheDocument());

beforeEach(() => {
  vi.clearAllMocks();
  useBTasksStore.setState({ tasks: [] });
  useDataSyncStore.getState().clear();
  useDataSyncStore.getState().setOverview(overview());
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Disabled,
  });
  vi.mocked(dataSyncApi.review).mockResolvedValue(reviewResult(laptopItems()));
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("the first sync review", () => {
  it("shows every item with its badge; held rows take no decision and never block Apply", async () => {
    renderReview();
    await loaded();

    expect(
      screen
        .getAllByTestId("data-sync-plan-item")
        .map((item) => item.getAttribute("data-type"))
        .sort(),
    ).toEqual(
      [Create, Update, Link, NeedsDecision, Held]
        .map((type) => DataSyncPlanItemTypeLabel[type])
        .sort(),
    );
    expect(within(row("link")).getByTestId("data-sync-plan-badge")).toHaveTextContent(
      "dataSync.plan.type.Link Rating",
    );
    expect(within(row("held")).queryByTestId("data-sync-plan-resolution")).toBeNull();
    expect(within(row("held")).getByTestId("data-sync-plan-held")).toHaveTextContent(
      "dataSync.plan.held.TooLarge",
    );
    // The link and the decision wait for the reader.
    expect(applyButton()).toBeDisabled();
    expect(screen.getAllByText("dataSync.review.decideFirst 2").length).toBeGreaterThan(0);

    fireEvent.click(screen.getByTestId("data-sync-plan-link-exact"));
    fireEvent.change(within(row("decide")).getByTestId("data-sync-plan-resolution"), {
      target: { value: String(DataSyncPlanResolution.Link) },
    });
    fireEvent.change(within(row("decide")).getByTestId("data-sync-plan-target"), {
      target: { value: "18" },
    });
    // Created 1 + changes 2 + linked 2.
    expect(applyButton()).toBeEnabled();
    expect(applyButton()).toHaveTextContent("dataSync.review.apply 5");

    vi.mocked(dataSyncApi.applyReview).mockResolvedValue({
      taskId: "DataSyncApply:review-1",
      decisionErrors: [],
    });
    await act(async () => {
      fireEvent.click(applyButton());
    });
    const [reviewId, input] = vi.mocked(dataSyncApi.applyReview).mock.calls[0];

    expect(reviewId).toBe("review-1");
    expect(input.backupBeforeDestructive).toBe(false);
    expect(input.decisions.map((decision) => decision.itemId)).toEqual([
      "customProperty/k/create",
      "customProperty/k/update",
      "customProperty/k/link",
      "customProperty/k/decide",
    ]);
    expect(input.decisions[3]).toMatchObject({
      resolution: DataSyncPlanResolution.Link,
      targetLocalKey: "18",
      reviewToken: "token-candidate-18",
    });
    expect(screen.getByTestId("data-sync-review-applying")).toBeInTheDocument();
  });

  it("says there is nothing to apply for a plan whose items are unchanged with known keys", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult([planItem("same", Unchanged, "Mood"), planItem("other", Unchanged, "Rating")]),
    );
    renderReview();
    await loaded();

    expect(applyButton()).toBeDisabled();
    expect(applyButton()).toHaveTextContent("dataSync.review.nothingToApply");
  });

  it("ticks changes one by one and by group, and keeps a node under an unticked parent out", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult([
        planItem("places", Update, "Places", {
          changes: [
            fieldChange("node:add:a", DataSyncFieldChangeKind.AddChild, "nodes", {
              path: ["Asia"],
            }),
            fieldChange(
              "node:add:b",
              DataSyncFieldChangeKind.AddChild,
              "nodes",
              { path: ["Asia", "Japan"] },
              { dependsOnChangeId: "node:add:a" },
            ),
            fieldChange(
              "node:rename:c",
              DataSyncFieldChangeKind.RenameChild,
              "nodes",
              { path: ["Europe"] },
              { from: { path: ["EU"] }, inUseCount: 7 },
            ),
          ],
        }),
      ]),
    );
    renderReview();
    await loaded();
    fireEvent.click(screen.getByTestId("data-sync-plan-expand"));
    const change = (id: string) =>
      document.querySelector<HTMLElement>(`[data-change="${id}"] input`)!;

    expect(change("node:add:b")).toBeEnabled();
    fireEvent.click(change("node:add:a"));
    expect(change("node:add:a")).not.toBeChecked();
    // Its child is out with it, and cannot be ticked alone.
    expect(change("node:add:b")).not.toBeChecked();
    expect(change("node:add:b")).toBeDisabled();
    // A rename says how many resources show the option.
    expect(screen.getByTestId("data-sync-change-in-use")).toHaveTextContent(
      "dataSync.plan.change.inUse 7",
    );

    const addGroup = document.querySelector<HTMLElement>(
      '[data-group="node:add"] [data-testid="data-sync-change-group"]',
    )!;

    fireEvent.click(addGroup);
    vi.mocked(dataSyncApi.applyReview).mockResolvedValue({ taskId: "t", decisionErrors: [] });
    await act(async () => {
      fireEvent.click(applyButton());
    });
    expect(
      vi.mocked(dataSyncApi.applyReview).mock.calls[0][1].decisions[0].excludedChangeIds,
    ).toEqual(["node:add:*"]);
  });

  it("pages changes not sent inline, and reads the review again when the plan moved on", async () => {
    const inline = fieldChange("tag:add:0", DataSyncFieldChangeKind.AddChild, "tags", {
      text: "Tag 0",
    });

    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult([
        planItem("tags", Update, "Genre", {
          changes: [inline],
          changeCounts: changeCounts({ add: 3 }),
          changesTruncated: true,
        }),
      ]),
    );
    vi.mocked(dataSyncApi.reviewChanges).mockResolvedValueOnce({
      planId: "0123456789abcdef",
      changes: [
        inline,
        fieldChange("tag:add:1", DataSyncFieldChangeKind.AddChild, "tags", { text: "Tag 1" }),
      ],
      warnings: [],
      total: 3,
    });
    renderReview();
    await loaded();
    fireEvent.click(screen.getByTestId("data-sync-plan-expand"));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-change-more"));
    });
    expect(dataSyncApi.reviewChanges).toHaveBeenCalledWith("review-1", {
      planId: "0123456789abcdef",
      itemId: "customProperty/k/tags",
      candidate: undefined,
      skip: 1,
      take: 200,
    });
    expect(screen.getAllByTestId("data-sync-change")).toHaveLength(2);

    vi.mocked(dataSyncApi.reviewChanges).mockResolvedValueOnce({
      planId: "0123456789abcdef",
      changes: [],
      warnings: [],
      total: 0,
      problem: { code: DataSyncProblemCode.PlanChanged },
    });
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-change-more"));
    });
    expect(dataSyncApi.review).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId("data-sync-review-banner")).toHaveTextContent(
      "dataSync.review.planChanged",
    );
  });

  it("says an added option merges into one here only in the tick state that folds it", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult([
        planItem("genre", Update, "Genre", {
          changes: [
            fieldChange(
              "ignoreCase",
              DataSyncFieldChangeKind.Set,
              "ignoreCase",
              { flag: true },
              { from: { flag: false } },
            ),
            fieldChange("choice:add:a", DataSyncFieldChangeKind.AddChild, "choices", {
              text: "action",
            }),
          ],
          warnings: [
            {
              code: DataSyncWarningCode.OptionLabelConflict,
              changeId: "choice:add:a",
              args: { when: "withIgnoreCaseChange", intoLabel: "Action" },
            },
          ],
        }),
      ]),
    );
    renderReview();
    await loaded();
    fireEvent.click(screen.getByTestId("data-sync-plan-expand"));

    expect(screen.getByTestId("data-sync-change-fold")).toHaveTextContent(
      "dataSync.plan.change.mergesInto Action",
    );
    fireEvent.click(document.querySelector<HTMLElement>('[data-change="ignoreCase"] input')!);
    expect(screen.queryByTestId("data-sync-change-fold")).toBeNull();
  });

  it("takes the fresh plan on DecisionsInvalid, keeping the decisions that still match", async () => {
    renderReview();
    await loaded();
    fireEvent.click(screen.getByTestId("data-sync-plan-link-exact"));
    fireEvent.change(within(row("decide")).getByTestId("data-sync-plan-resolution"), {
      target: { value: String(DataSyncPlanResolution.Skip) },
    });
    const fresh = laptopItems().map((item) =>
      item.itemId === "customProperty/k/link"
        ? { ...item, candidates: [planCandidate("15", "Rating", { reviewToken: "moved" })] }
        : item,
    );

    vi.mocked(dataSyncApi.applyReview).mockResolvedValue({
      problem: { code: DataSyncProblemCode.DecisionsInvalid },
      decisionErrors: [
        { itemId: "customProperty/k/link", code: DataSyncDecisionErrorCode.ChangedSinceReview },
      ],
      plan: { ...planOf(fresh), planId: "fedcba9876543210" },
    });
    await act(async () => {
      fireEvent.click(applyButton());
    });

    expect(screen.getByTestId("data-sync-review-banner")).toHaveTextContent(
      "dataSync.review.decisionsInvalid",
    );
    expect(within(row("link")).getByTestId("data-sync-plan-error")).toHaveTextContent(
      "dataSync.decisionError.ChangedSinceReview",
    );
    // The skip still stands; the link waits to be confirmed again.
    expect(within(row("decide")).getByTestId("data-sync-plan-resolution")).toHaveValue(
      String(DataSyncPlanResolution.Skip),
    );
    expect(row("link")).toHaveAttribute("data-pending", "true");
    expect(applyButton()).toBeDisabled();
  });

  it("waits for another task, and cancels through the review, never the task list", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applying,
        taskId: "DataSyncApply:review-1",
      }),
    );
    useBTasksStore.setState({
      tasks: [
        {
          id: "DataSyncApply:review-1",
          name: "Apply",
          status: BTaskStatus.NotStarted,
          reasonForUnableToStart: "Enhancement",
          createdAt: "2026-09-01 08:00:00.000",
          isPersistent: true,
          type: 0,
          resourceType: 0,
        } as never,
      ],
    });
    renderReview();
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-waiting")).toHaveTextContent(
        "dataSync.review.waitingFor Enhancement",
      ),
    );

    vi.mocked(dataSyncApi.cancelReviewApply).mockResolvedValue({
      state: DataSyncReviewState.Staged,
    });
    vi.mocked(dataSyncApi.review).mockResolvedValue(reviewResult(laptopItems()));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-cancel"));
    });
    expect(dataSyncApi.cancelReviewApply).toHaveBeenCalledWith("review-1");
    await loaded();
    expect(screen.queryByTestId("data-sync-review-applying")).toBeNull();
  });

  it("says what was done, and lists what changed while the reader reviewed", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), { state: DataSyncReviewState.Applied, applyLogId: 7 }),
    );
    vi.mocked(dataSyncApi.historyEntry).mockResolvedValue({
      entry: historyEntry(7, DataSyncHistoryKind.FirstLink, {
        counts: historyCountsOf({ created: 1, updated: 1, linked: 2, changedSinceReview: 1 }),
      }),
      items: [
        {
          itemId: "customProperty/k/update",
          kind: "customProperty",
          name: "Genre",
          outcome: DataSyncItemOutcome.ChangedSinceReview,
          action: DataSyncItemAction.None,
          type: Update,
        },
      ],
    });
    const { onChanged } = renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-done")).toHaveTextContent(
        "dataSync.review.inStep Laptop",
      ),
    );
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-changed")).toHaveTextContent("Genre"),
    );
    expect(screen.getByTestId("data-sync-review-counts")).toHaveTextContent(
      "dataSync.review.counts 4 0 1",
    );
    expect(onChanged).toHaveBeenCalled();
    expect(screen.queryByTestId("data-sync-review-follow-up")).toBeNull();
  });

  it("after a copy once, offers to keep receiving, to keep in step, or to stop reading", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applied,
        copyOnce: true,
        linkMode: DataSyncLinkMode.Off,
        linkId: 21,
      }),
    );
    const { onClose } = renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    expect(screen.getByTestId("data-sync-review-done")).toHaveTextContent(
      "dataSync.review.copied Laptop",
    );
    expect(screen.getByTestId("data-sync-review-keep-in-step")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-keep-receiving"));
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(21, { mode: DataSyncLinkMode.Follow });
    expect(onClose).toHaveBeenCalled();

    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-stop-reading"));
    });
    expect(dataSyncApi.forgetAccess).toHaveBeenCalledWith("node-laptop");
  });

  it("asks before keeping in step both ways, saying only what it turns on here", async () => {
    // Sharing is on already; remote access is off.
    useDataSyncStore
      .getState()
      .setOverview(overview({ sharingEnabled: true, remoteAccessMode: RemoteAccessMode.Disabled }));
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applied,
        copyOnce: true,
        linkMode: DataSyncLinkMode.Off,
        linkId: 21,
      }),
    );
    const { onClose } = renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByTestId("data-sync-review-keep-in-step"));
    const question = screen.getByTestId("data-sync-review-confirm");

    expect(question).toHaveTextContent("dataSync.twoWay.title Laptop");
    expect(question).toHaveTextContent("dataSync.twoWay.consent Laptop");
    expect(screen.getByTestId("data-sync-review-confirm-warning")).toHaveTextContent(
      /^dataSync\.sharing\.remoteAccess$/,
    );
    expect(within(question).getByRole("heading")).toHaveFocus();
    expect(dataSyncApi.setSharing).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).not.toHaveBeenCalled();

    // Escape answers the question, not the review.
    fireEvent.keyDown(question, { key: "Escape" });
    expect(screen.queryByTestId("data-sync-review-confirm")).toBeNull();
    expect(screen.getByTestId("data-sync-review-keep-in-step")).toHaveFocus();
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("data-sync-review-keep-in-step"));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-confirm-yes"));
    });
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(21, { mode: DataSyncLinkMode.TwoWay });
    expect(onClose).toHaveBeenCalled();
  });

  it("turns nothing on, and says nothing of it, where both are on already", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applied,
        copyOnce: true,
        linkMode: DataSyncLinkMode.Off,
        linkId: 21,
      }),
    );
    renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByTestId("data-sync-review-keep-in-step"));
    expect(screen.queryByTestId("data-sync-review-confirm-warning")).toBeNull();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-confirm-yes"));
    });
    expect(dataSyncApi.setSharing).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(21, { mode: DataSyncLinkMode.TwoWay });
  });

  it("keeps in step both ways only where this window may create access", async () => {
    useRemoteAccessStore.setState({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
    });
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applied,
        copyOnce: true,
        linkId: 21,
      }),
    );
    renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    expect(screen.queryByTestId("data-sync-review-keep-in-step")).toBeNull();
    expect(screen.getByTestId("data-sync-review-keep-receiving")).toBeInTheDocument();
  });

  it("keeps in step both ways only where the server says this caller may create access", async () => {
    // The window takes itself for this device's own — its defaults when who is looking could
    // not be read — but the server knows better.
    useDataSyncStore.getState().setOverview(overview({ canManageSharing: false }));
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(laptopItems(), {
        state: DataSyncReviewState.Applied,
        copyOnce: true,
        linkId: 21,
      }),
    );
    renderReview();

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    expect(screen.queryByTestId("data-sync-review-keep-in-step")).toBeNull();
    expect(screen.getByTestId("data-sync-review-keep-receiving")).toBeInTheDocument();
  });

  it("says what two-way sends back, marks what is newer here, and fetches again", async () => {
    vi.mocked(dataSyncApi.review).mockResolvedValue(
      reviewResult(
        [
          planItem("newer", Unchanged, "Mood", { reason: DataSyncPlanItemReason.LocalIsNewer }),
          planItem("create", Create, "Studio"),
        ],
        { linkMode: DataSyncLinkMode.TwoWay },
      ),
    );
    renderReview();
    await loaded();

    expect(screen.getByTestId("data-sync-review-two-way")).toHaveTextContent(
      "dataSync.review.twoWayNote Laptop",
    );
    // Unchanged rows are filtered out until asked for.
    fireEvent.click(
      document.querySelector<HTMLElement>(
        '[data-testid="data-sync-plan-type-chip"][data-type="Unchanged"]',
      )!,
    );
    expect(within(row("newer")).getByTestId("data-sync-plan-newer-here")).toHaveTextContent(
      "dataSync.plan.newerHere",
    );
    expect(screen.getByTestId("data-sync-review-fetched")).toHaveTextContent(
      "dataSync.review.fetched",
    );
    vi.mocked(dataSyncApi.refetchReview).mockResolvedValue(reviewResult(laptopItems()));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-refetch"));
    });
    expect(dataSyncApi.refetchReview).toHaveBeenCalledWith("review-1");
    expect(screen.getAllByTestId("data-sync-plan-item").length).toBe(5);
  });

  it("explains Skip, and says a review that expired", async () => {
    renderReview();
    await loaded();
    fireEvent.change(within(row("create")).getByTestId("data-sync-plan-resolution"), {
      target: { value: String(DataSyncPlanResolution.Skip) },
    });
    expect(row("create")).toHaveTextContent("dataSync.plan.skipHint Laptop");
    cleanup();

    vi.mocked(dataSyncApi.review).mockResolvedValue({
      copyOnce: false,
      linkMode: DataSyncLinkMode.Follow,
      problem: { code: DataSyncProblemCode.ReviewExpired },
    });
    renderReview();
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-gone")).toHaveTextContent(
        "dataSync.problem.ReviewExpired",
      ),
    );
  });

  it("says the review is being fetched while the link has none yet", () => {
    renderReview({ reviewId: undefined });

    expect(screen.getByTestId("data-sync-review-preparing")).toHaveTextContent(
      "dataSync.wizard.fetching Laptop",
    );
    expect(dataSyncApi.review).not.toHaveBeenCalled();
  });

  it("says the link waits for an approval there, not that a review is on its way", () => {
    renderReview({ reviewId: undefined, awaitingAccess: true });

    expect(screen.queryByTestId("data-sync-review-preparing")).toBeNull();
    expect(screen.getByTestId("data-sync-review-awaiting-access")).toHaveTextContent(
      "dataSync.status.AwaitingAccess Laptop",
    );
    expect(screen.getByTestId("data-sync-review-awaiting-access")).toHaveTextContent(
      "dataSync.link.approveThere Laptop",
    );
  });

  it("offers data sync's help beside its title", () => {
    renderReview({ reviewId: undefined });

    expect(within(screen.getByTestId("data-sync-review")).getByTestId("help")).toHaveAttribute(
      "data-help",
      "multiDevice/dataSync",
    );
  });
});
