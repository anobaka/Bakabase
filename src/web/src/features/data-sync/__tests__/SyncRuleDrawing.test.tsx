import type * as Api from "../api";
import type { SyncPeer } from "../viewModels";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import SyncRuleDrawing from "../components/SyncRuleDrawing";
import { dataSyncApi } from "../api";
import { newSyncPeer, syncPeerOf } from "../viewModels";

import { link, mapPeer, NOW, recordingActions } from "./dataSyncFixtures";

import {
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncResumeAction,
  RemoteAccessMode,
} from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  // Keys as text, followed by the interpolated values, so a test can see what was said.
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
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    updateLink: vi.fn(async () => ({})),
    createLink: vi.fn(async () => ({})),
    setSharing: vi.fn(async () => undefined),
    revokeReader: vi.fn(async () => undefined),
    resumeLink: vi.fn(async () => ({})),
    pauseLink: vi.fn(async () => ({})),
    syncNow: vi.fn(async () => ({})),
    resetLink: vi.fn(async () => undefined),
    setAllPaused: vi.fn(async () => undefined),
    copyOnce: vi.fn(async () => ({
      link: link(21, "node-nas", "NAS", { mode: 0, state: DataSyncLinkState.AwaitingReview }),
    })),
    links: vi.fn(async () => []),
  },
}));

const nas = (patch: Parameters<typeof link>[3] = {}) =>
  syncPeerOf(mapPeer("node-nas", "NAS", patch));

let recorded = recordingActions();

const draw = (
  peer: SyncPeer,
  options: {
    canManage?: boolean;
    sharingEnabled?: boolean;
    remoteAccessMode?: RemoteAccessMode;
    onCreateCode?: () => void;
  } = {},
) =>
  render(
    <MemoryRouter>
      <SyncRuleDrawing
        actions={recorded.actions}
        canManage={options.canManage ?? true}
        now={NOW}
        peer={peer}
        remoteAccessMode={options.remoteAccessMode ?? RemoteAccessMode.Enabled}
        selfName="This PC"
        sharingEnabled={options.sharingEnabled ?? true}
        onCreateCode={options.onCreateCode}
      />
      <Where />
    </MemoryRouter>,
  );

/** Where the window is: what a control navigated to. */
function Where() {
  const location = useLocation();

  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

const receive = () => screen.getByTestId("data-sync-arrow-receive");
const read = () => screen.getByTestId("data-sync-arrow-read");
const lastConfirmation = () => recorded.confirmations[recorded.confirmations.length - 1];

beforeEach(() => {
  vi.clearAllMocks();
  recorded = recordingActions();
});
afterEach(cleanup);

describe("the rule editor's arrows", () => {
  it("are drawn, never pressed, and each says its direction", () => {
    draw(nas());

    expect(receive().tagName).not.toBe("BUTTON");
    expect(receive()).toHaveAttribute("data-status", "active");
    expect(receive()).toHaveAccessibleName("federation.map.direction.sync.in.active NAS");
    expect(read()).toHaveAttribute("data-status", "active");
    expect(read()).toHaveAccessibleName("federation.map.direction.sync.out.active NAS");
  });

  it("turns receiving off, asking first and saying the other still reads", () => {
    draw(nas());

    fireEvent.click(screen.getByTestId("data-sync-mode-off"));
    expect(recorded.actions.confirm).toHaveBeenCalledTimes(1);
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.off.title NAS",
      description: "dataSync.off.description NAS",
      warning: "dataSync.off.stillReads NAS",
      refresh: ["dataSync"],
    });
    // Nothing happens until it is confirmed, and then through the host.
    expect(dataSyncApi.updateLink).not.toHaveBeenCalled();
    void lastConfirmation().action();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Off });
  });

  it("turns a stopped link back on in the mode picked", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.Stopped,
      }),
    );

    expect(receive()).toHaveAttribute("data-status", "none");
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    });
    // Resuming a link it already has creates no access: it just runs, through the host.
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Follow });
  });

  it("stops the other device reading, asking first, whether receiving is on or off", async () => {
    draw(nas({ mode: DataSyncLinkMode.Off, state: DataSyncLinkState.Stopped }));

    fireEvent.click(screen.getByTestId("data-sync-stop-reading"));
    expect(lastConfirmation()).toMatchObject({ title: "dataSync.arrow.read.stopTitle NAS" });
    await lastConfirmation().action();
    expect(dataSyncApi.revokeReader).toHaveBeenCalledWith("node-nas");
  });

  it("offers ways to let the other device read, where this window may", () => {
    const onCreateCode = vi.fn();

    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        onCreateCode,
      },
    );
    fireEvent.click(screen.getByText("dataSync.invitation.createFor NAS"));
    expect(onCreateCode).toHaveBeenCalled();
    // Nothing to stop where it does not read this device.
    expect(read()).toHaveAttribute("data-status", "none");
    expect(screen.queryByTestId("data-sync-stop-reading")).toBeNull();
    cleanup();

    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        onCreateCode,
        canManage: false,
      },
    );
    expect(screen.queryByText("dataSync.invitation.createFor NAS")).toBeNull();
  });

  it("shows the kinds the other device receives on the may-read arrow", () => {
    draw(nas({ peerKinds: ["extensionGroup"] }));

    expect(
      within(screen.getByTestId("data-sync-peer-kinds")).getByText("dataSync.kind.extensionGroup"),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId("data-sync-peer-kinds")).queryByText(
        "dataSync.kind.customProperty",
      ),
    ).toBeNull();
  });
});

describe("the kinds a link receives", () => {
  it("switches a kind through the host", async () => {
    draw(nas());
    const chips = screen.getAllByTestId("data-sync-kind-chip");

    await act(async () => {
      fireEvent.click(chips.find((chip) => chip.dataset.kind === "customProperty")!);
    });
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { kinds: ["extensionGroup"] });
  });

  it("never switches the last one off", () => {
    draw(nas({ kinds: ["extensionGroup"] }));
    const last = screen
      .getAllByTestId("data-sync-kind-chip")
      .find((chip) => chip.dataset.kind === "extensionGroup")!;

    expect(last).toHaveAttribute("aria-disabled", "true");
    expect(last).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(last);
    expect(recorded.actions.run).not.toHaveBeenCalled();
  });
});

describe("the mode buttons", () => {
  it("are buttons in a named group, never radios the arrow keys would select", () => {
    draw(nas());
    const group = screen.getByTestId("data-sync-modes");
    const twoWay = screen.getByTestId("data-sync-mode-twoWay");

    expect(group).toHaveAttribute("role", "group");
    expect(group).toHaveAccessibleName("dataSync.mode.legend NAS");
    expect(within(group).queryAllByRole("radio")).toHaveLength(0);
    expect(twoWay.tagName).toBe("BUTTON");
    expect(twoWay).toHaveAttribute("aria-pressed", "true");
  });

  it("change nothing when the arrow keys move over them: only a press changes the link", () => {
    draw(nas());
    const twoWay = screen.getByTestId("data-sync-mode-twoWay");

    act(() => twoWay.focus());
    // A radio group would have turned the link to Receive only on ArrowLeft, and asked to stop
    // it on ArrowRight (wrapping to Off): neither may happen from looking at the options.
    for (const key of ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]) {
      fireEvent.keyDown(twoWay, { key });
      fireEvent.keyUp(twoWay, { key });
    }
    expect(twoWay).toHaveFocus();
    expect(twoWay).toHaveAttribute("aria-pressed", "true");
    expect(recorded.actions.run).not.toHaveBeenCalled();
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).not.toHaveBeenCalled();

    // Pressing one does: Receive only, on a link the other device already reads, just runs.
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Follow });
  });

  it("asks for the two-way consent and turns sharing and remote access on when either is off", async () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      { sharingEnabled: false, remoteAccessMode: RemoteAccessMode.Disabled },
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(lastConfirmation()).toMatchObject({
      description: "dataSync.twoWay.consent NAS",
      warning: "dataSync.twoWay.turnsOnSharing dataSync.sharing.remoteAccess",
    });
    await lastConfirmation().action();
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.TwoWay });
  });

  it("says nothing about turning things on when both are on already", () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(lastConfirmation().description).toBe("dataSync.twoWay.consent NAS");
    expect(lastConfirmation().warning).toBeUndefined();
  });

  it("previews the request the other device will see before a new link sends it", async () => {
    draw(newSyncPeer("node-new", "New PC"));
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.follow.title New PC",
      warning: "dataSync.request.follow This PC",
    });
    await lastConfirmation().action();
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "node-new",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
  });

  it("does not offer what would create access where this window may not", () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      {
        canManage: false,
      },
    );

    expect(screen.getByTestId("data-sync-mode-twoWay")).toBeDisabled();
    expect(screen.getByTestId("data-sync-mode-off")).not.toBeDisabled();
    expect(screen.getByTestId("data-sync-manage-elsewhere")).toBeInTheDocument();
  });

  it("copies once through a confirmation, then opens its first sync", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.Stopped,
      }),
    );

    fireEvent.click(screen.getByTestId("data-sync-copy-once"));
    await act(async () => {
      await lastConfirmation().action();
    });
    expect(dataSyncApi.copyOnce).toHaveBeenCalledWith({
      peerNodeId: "node-nas",
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(screen.getByTestId("location")).toHaveTextContent("/data-sync?link=21&review=1");
    expect(recorded.actions.setNotice).not.toHaveBeenCalled();
  });

  it("says a copy once asked for access, and opens no first sync that cannot come yet", async () => {
    vi.mocked(dataSyncApi.copyOnce).mockResolvedValueOnce({
      link: link(21, "node-nas", "NAS", {
        mode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.AwaitingAccess,
      }),
    });
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.Stopped,
      }),
    );

    fireEvent.click(screen.getByTestId("data-sync-copy-once"));
    await act(async () => {
      await lastConfirmation().action();
    });
    expect(recorded.actions.setNotice).toHaveBeenCalledWith("dataSync.wizard.requested NAS");
    expect(screen.getByTestId("location")).toHaveTextContent(/^\/$/);
  });

  it("says only what is off when keeping in step both ways turns it on", () => {
    draw(
      nas({ mode: DataSyncLinkMode.Follow, peerMayReadUs: false, peerModeTowardsUs: undefined }),
      { sharingEnabled: true, remoteAccessMode: RemoteAccessMode.Disabled },
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.twoWay.title NAS",
      description: "dataSync.twoWay.consent NAS",
      warning: "dataSync.sharing.remoteAccess",
    });
  });
});

describe("what the rule editor keeps offering", () => {
  it("turns sharing on before it makes a code that could not work otherwise", async () => {
    const onCreateCode = vi.fn();
    const reader = nas({
      mode: DataSyncLinkMode.Follow,
      peerMayReadUs: false,
      peerModeTowardsUs: undefined,
    });

    draw(reader, { onCreateCode, sharingEnabled: false });
    fireEvent.click(screen.getByTestId("data-sync-create-code-for"));
    expect(onCreateCode).not.toHaveBeenCalled();
    expect(lastConfirmation()).toMatchObject({
      title: "dataSync.sharing.onTitle",
      warning: "dataSync.twoWay.turnsOnSharing",
    });
    await lastConfirmation().action();
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: false,
    });
    expect(onCreateCode).toHaveBeenCalledTimes(1);
    cleanup();

    draw(reader, { onCreateCode, remoteAccessMode: RemoteAccessMode.Disabled });
    fireEvent.click(screen.getByTestId("data-sync-create-code-for"));
    expect(lastConfirmation().warning).toBe("dataSync.sharing.remoteAccess");
  });

  it("offers to start without the other device's review once it has waited a week", async () => {
    draw(
      nas({
        state: DataSyncLinkState.WaitingForPeerReview,
        startAnywayAt: "2026-08-31 08:00:00.000",
      }),
    );

    expect(screen.getByText("dataSync.pause.startAnywayHint NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-start-anyway"));
    });
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, DataSyncResumeAction.StartAnyway);
    cleanup();

    // Not before, and not where the runtime has not said when.
    draw(
      nas({
        state: DataSyncLinkState.WaitingForPeerReview,
        startAnywayAt: "2026-09-02 08:00:00.000",
      }),
    );
    expect(screen.queryByTestId("data-sync-start-anyway")).toBeNull();
    cleanup();
    draw(nas({ state: DataSyncLinkState.WaitingForPeerReview }));
    expect(screen.queryByTestId("data-sync-start-anyway")).toBeNull();
  });
});

describe("the status line", () => {
  it("offers what a pause asks for", () => {
    draw(nas({ state: DataSyncLinkState.Paused, pausedReason: 4 }));

    expect(
      screen.getByText("dataSync.status.paused.PeerIdentityDuplicated NAS"),
    ).toBeInTheDocument();
    expect(screen.getByText("dataSync.pause.resume")).toBeInTheDocument();
  });

  it("asks for access again when the other device revoked it, not when it turned sharing off", async () => {
    draw(nas({ lastErrorCode: "AccessRevoked" }));

    expect(screen.getByText("dataSync.status.AccessRevoked NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("dataSync.pause.askAgain NAS"));
    });
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, DataSyncResumeAction.AskAccessAgain);
    cleanup();

    // The same line for sharing turned off there: asking cannot help, so it is not offered.
    draw(nas({ lastErrorCode: "PeerSharingOff" }));
    expect(screen.getByText("dataSync.status.AccessRevoked NAS")).toBeInTheDocument();
    expect(screen.queryByText("dataSync.pause.askAgain NAS")).toBeNull();
  });

  it("asks the other device to keep in step when it declined to read back", async () => {
    draw(nas({ readBackDeclined: true, peerMayReadUs: false, peerModeTowardsUs: undefined }));

    expect(screen.getByText("dataSync.status.ReadBackDeclined NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("dataSync.link.askKeepInStep NAS"));
    });
    // On a working two-way link the runtime answers this action with an ordinary two-way
    // request and leaves the link's state alone (its reset recovery is for a paused link).
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, DataSyncResumeAction.AskAccessAgain);
    cleanup();

    // Paused as reset, the same action would start over: not offered for the read-back.
    draw(
      nas({
        readBackDeclined: true,
        peerMayReadUs: false,
        peerModeTowardsUs: undefined,
        state: DataSyncLinkState.Paused,
        pausedReason: DataSyncPauseReason.PeerReset,
      }),
    );
    expect(screen.getByText("dataSync.status.ReadBackDeclined NAS")).toBeInTheDocument();
    expect(screen.queryByText("dataSync.link.askKeepInStep NAS")).toBeNull();
  });

  it("says reading the other device back failed, and offers to try again where this window may", async () => {
    const failed = nas({
      state: DataSyncLinkState.AwaitingAccess,
      initiator: DataSyncLinkInitiator.Peer,
      // As the server says it: the failure, and why in its detail.
      lastErrorCode: "ReadBackFailed",
      lastErrorDetail: "Unreachable",
      peerModeTowardsUs: "twoWay",
    });

    draw(failed);
    const status = screen.getByTestId("data-sync-status");

    expect(within(status).getByText(/dataSync\.status\.ReadBackFailed/)).toHaveTextContent(
      "dataSync.status.ReadBackFailed NAS dataSync.peerError.Unreachable",
    );
    // Nothing waits for an approval there: no hint to approve it on the other device.
    expect(within(status).queryByText(/dataSync\.link\.approveThere/)).toBeNull();
    expect(within(status).getByText("dataSync.link.readBackAgain NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-read-back-again"));
    });
    expect(recorded.actions.run).toHaveBeenCalled();
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, DataSyncResumeAction.AskAccessAgain);
    cleanup();

    // A request creates access: not offered where this window may not.
    draw(failed, { canManage: false });
    expect(screen.queryByTestId("data-sync-read-back-again")).toBeNull();
    expect(screen.getByText("dataSync.manageElsewhere")).toBeInTheDocument();
  });

  it("says mutual Follow works as both ways", () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, peerModeTowardsUs: "follow" }));

    expect(screen.getByText("dataSync.status.MutualFollow")).toBeInTheDocument();
  });

  it("says what waits for a decision on the other device", () => {
    draw(
      nas({
        peerAttention: {
          headless: true,
          openDecisions: 2,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
    );

    expect(screen.getByText("dataSync.status.NeedsYouThere NAS 2")).toBeInTheDocument();
    expect(screen.getByText("dataSync.link.decideThere NAS")).toBeInTheDocument();
  });

  it("offers the review while it waits", () => {
    draw(nas({ state: DataSyncLinkState.AwaitingReview }));

    expect(screen.getByText("dataSync.link.review").closest("a")).toHaveAttribute(
      "href",
      "/data-sync?link=1&review=1",
    );
  });
});
