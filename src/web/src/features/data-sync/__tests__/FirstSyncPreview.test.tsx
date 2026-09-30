import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FirstSyncPreview from "../components/FirstSyncPreview";
import { dataSyncApi } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import { bTask, firstSyncPreview, overview, previewEntry } from "./dataSyncFixtures";

import {
  BTaskStatus,
  ClientMode,
  DataSyncFirstSyncAction,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncNaturalMatch,
  DataSyncPreviewOutcome,
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
    firstSync: vi.fn(),
    startFirstSync: vi.fn(async () => ({ taskId: "DataSyncReview:3" })),
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

const { Create, Update, Unchanged, NameMatch, Question, Held } = DataSyncPreviewOutcome;
const initialRemote = useRemoteAccessStore.getState();
const rating = previewEntry("n1", NameMatch, "Rating", {
  candidates: [
    { localKey: "15", name: "Rating", match: DataSyncNaturalMatch.Exact, updatable: true },
    { localKey: "16", name: "rating", match: DataSyncNaturalMatch.Similar, updatable: false },
  ],
});
const laptop = () => [
  previewEntry("c1", Create, "Studio"),
  previewEntry("u1", Update, "Genre"),
  rating,
  previewEntry("q1", Question, "Artist"),
  previewEntry("h1", Held, "Huge tags"),
  previewEntry("s1", Unchanged, "Mood"),
];

const renderPreview = () => {
  const onClose = vi.fn();
  const onChanged = vi.fn();

  render(
    <MemoryRouter>
      <FirstSyncPreview
        linkId={3}
        peerName="Laptop"
        peerNodeId="node-laptop"
        onChanged={onChanged}
        onClose={onClose}
      />
    </MemoryRouter>,
  );

  return { onClose, onChanged };
};

/** Starts what is shown, then lets the task end and the link move on as the server would. */
const startAndFinish = async (after = DataSyncLinkState.Active) => {
  await act(async () => {
    fireEvent.click(screen.getByTestId("data-sync-review-start"));
  });
  vi.mocked(dataSyncApi.firstSync).mockResolvedValue(
    firstSyncPreview([], {
      state: after,
      source: undefined,
      copyOnce: after === DataSyncLinkState.Stopped,
    }),
  );
  act(() =>
    useBTasksStore.setState({
      tasks: [bTask("DataSyncReview:3", BTaskStatus.Completed, "2026-09-01 08:00:00.000")],
    }),
  );
};

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
  vi.mocked(dataSyncApi.firstSync).mockResolvedValue(firstSyncPreview(laptop()));
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("the first sync", () => {
  it("shows what the merge would do, and starts it with what was left out", async () => {
    renderPreview();
    await waitFor(() => expect(screen.getAllByTestId("data-sync-review-group")).toHaveLength(5));

    expect(
      screen
        .getAllByTestId("data-sync-review-group")
        .map((group) => group.getAttribute("data-group")),
    ).toEqual(["Create", "Update", "NameMatch", "Question", "Held"]);
    expect(screen.getByTestId("data-sync-review-same")).toHaveTextContent("dataSync.review.same 1");
    expect(screen.getByText("dataSync.review.hint.NameMatch Laptop")).toBeInTheDocument();
    // What is held takes no choice.
    expect(screen.getAllByTestId("data-sync-review-skip")).toHaveLength(4);

    fireEvent.click(screen.getAllByTestId("data-sync-review-skip")[0]);
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-start"));
    });
    expect(dataSyncApi.startFirstSync).toHaveBeenCalledWith(3, [
      { kind: "customProperty", key: "c1", action: DataSyncFirstSyncAction.Skip },
    ]);
    expect(screen.getByTestId("data-sync-review-applying")).toHaveTextContent(
      "dataSync.review.waiting",
    );
  });

  it("says what waits under Needs you once it is done", async () => {
    const { onChanged } = renderPreview();

    await waitFor(() => expect(screen.getByTestId("data-sync-review-start")).toBeInTheDocument());

    await startAndFinish();

    await waitFor(() => expect(screen.getByTestId("data-sync-review-done")).toBeInTheDocument());
    expect(screen.getByTestId("data-sync-review-done")).toHaveTextContent(
      "dataSync.review.inStep Laptop",
    );
    expect(screen.getByTestId("data-sync-review-done")).toHaveTextContent(
      "dataSync.review.askedAfter 2",
    );
    expect(onChanged).toHaveBeenCalled();
  });

  it("answers a copy once's name matches in the preview, then offers to keep receiving or stop reading", async () => {
    vi.mocked(dataSyncApi.firstSync).mockResolvedValue(
      firstSyncPreview([rating], { copyOnce: true, mode: DataSyncLinkMode.Off }),
    );
    const { onClose } = renderPreview();
    const choice = await screen.findByLabelText("dataSync.review.choice.label Rating");

    // Only a definition of the same type can be linked to.
    expect(
      within(choice)
        .getAllByRole("option")
        .map((option) => option.getAttribute("value")),
    ).toEqual(["", "15", "keepBoth"]);
    fireEvent.change(choice, { target: { value: "15" } });
    await startAndFinish(DataSyncLinkState.Stopped);
    expect(dataSyncApi.startFirstSync).toHaveBeenCalledWith(3, [
      { kind: "customProperty", key: "n1", action: DataSyncFirstSyncAction.Link, localKey: "15" },
    ]);

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    expect(screen.getByTestId("data-sync-review-done")).toHaveTextContent(
      "dataSync.review.copied Laptop",
    );
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-keep-receiving"));
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(3, { mode: DataSyncLinkMode.Follow });
    expect(onClose).toHaveBeenCalled();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-stop-reading"));
    });
    expect(dataSyncApi.forgetAccess).toHaveBeenCalledWith("node-laptop");
  });

  it("asks before keeping in step both ways after a copy once, saying only what it turns on here", async () => {
    useDataSyncStore
      .getState()
      .setOverview(overview({ sharingEnabled: true, remoteAccessMode: RemoteAccessMode.Disabled }));
    vi.mocked(dataSyncApi.firstSync).mockResolvedValue(
      firstSyncPreview([], { copyOnce: true, mode: DataSyncLinkMode.Off }),
    );
    const { onClose } = renderPreview();

    await waitFor(() => expect(screen.getByTestId("data-sync-review-start")).toBeInTheDocument());
    await startAndFinish(DataSyncLinkState.Stopped);
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByTestId("data-sync-review-keep-in-step"));
    const question = screen.getByTestId("data-sync-review-confirm");

    expect(question).toHaveTextContent("dataSync.twoWay.consent Laptop");
    expect(screen.getByTestId("data-sync-review-confirm-warning")).toHaveTextContent(
      /^dataSync\.sharing\.remoteAccess$/,
    );
    expect(within(question).getByRole("heading")).toHaveFocus();
    // Escape answers the question, not the preview.
    fireEvent.keyDown(question, { key: "Escape" });
    expect(screen.queryByTestId("data-sync-review-confirm")).toBeNull();
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("data-sync-review-keep-in-step"));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-review-confirm-yes"));
    });
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(3, { mode: DataSyncLinkMode.TwoWay });
  });

  it("keeps in step both ways only where the server says this caller may create access", async () => {
    useDataSyncStore.getState().setOverview(overview({ canManageSharing: false }));
    vi.mocked(dataSyncApi.firstSync).mockResolvedValue(firstSyncPreview([], { copyOnce: true }));
    renderPreview();
    await waitFor(() => expect(screen.getByTestId("data-sync-review-start")).toBeInTheDocument());
    await startAndFinish(DataSyncLinkState.Stopped);

    await waitFor(() =>
      expect(screen.getByTestId("data-sync-review-follow-up")).toBeInTheDocument(),
    );
    expect(screen.queryByTestId("data-sync-review-keep-in-step")).toBeNull();
  });

  it("says the snapshot is being fetched, or that the link waits for an approval there", async () => {
    vi.mocked(dataSyncApi.firstSync).mockResolvedValue(firstSyncPreview([], { source: undefined }));
    renderPreview();
    expect(await screen.findByTestId("data-sync-review-preparing")).toHaveTextContent(
      "dataSync.wizard.fetching Laptop",
    );
    expect(within(screen.getByTestId("data-sync-review")).getByTestId("help")).toHaveAttribute(
      "data-help",
      "multiDevice/dataSync",
    );
    cleanup();

    vi.mocked(dataSyncApi.firstSync).mockResolvedValue(
      firstSyncPreview([], { source: undefined, state: DataSyncLinkState.AwaitingAccess }),
    );
    renderPreview();
    expect(await screen.findByTestId("data-sync-review-awaiting-access")).toHaveTextContent(
      "dataSync.link.approveThere Laptop",
    );
  });
});
