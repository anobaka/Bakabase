import type * as Api from "../api";
import type { SyncPeer } from "../viewModels";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import SyncRuleDrawing from "../components/SyncRuleDrawing";
import { dataSyncApi } from "../api";
import { syncPeerFromLink, syncPeerFromMapPeer } from "../viewModels";

import { link, mapPeer, NOW, recordingActions } from "./dataSyncFixtures";
import EscapableDetails from "./EscapableDetails";

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
    updateLink: vi.fn(async () => ({})),
    createLink: vi.fn(async () => ({})),
    setSharing: vi.fn(async () => undefined),
    revokeReader: vi.fn(async () => undefined),
    resumeLink: vi.fn(async () => ({})),
    pauseLink: vi.fn(async () => ({})),
    syncNow: vi.fn(async () => ({})),
    resetLink: vi.fn(async () => undefined),
    setAllPaused: vi.fn(async () => undefined),
    copyOnce: vi.fn(async () => ({ linkId: 21, copyOnce: true, linkMode: 0 })),
    links: vi.fn(async () => []),
  },
}));

const nas = (patch: Parameters<typeof link>[3] = {}) =>
  syncPeerFromLink(link(1, "node-nas", "NAS", patch));

let recorded = recordingActions();

const draw = (
  peer: SyncPeer,
  options: {
    canManage?: boolean;
    sharingEnabled?: boolean;
    remoteAccessMode?: RemoteAccessMode;
    initialWidth?: number;
    onCreateCode?: () => void;
  } = {},
) =>
  render(
    <MemoryRouter>
      <SyncRuleDrawing
        actions={recorded.actions}
        canManage={options.canManage ?? true}
        initialWidth={options.initialWidth ?? 520}
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
  it("are buttons that say whether they are on", () => {
    draw(nas());

    expect(receive().tagName).toBe("BUTTON");
    expect(receive()).toHaveAttribute("aria-pressed", "true");
    expect(read().tagName).toBe("BUTTON");
    expect(read()).toHaveAttribute("aria-pressed", "true");
    expect(receive()).toHaveAccessibleName(
      "federation.map.direction.sync.in.active NAS, dataSync.mode.twoWay",
    );
  });

  it("turns receiving off from the keyboard, asking first and saying the other still reads", () => {
    draw(nas());

    fireEvent.keyDown(receive(), { key: "Enter" });
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

  it("turns a stopped link back on in its last mode with Space", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.Stopped,
      }),
    );

    expect(receive()).toHaveAttribute("aria-pressed", "false");
    await act(async () => {
      fireEvent.keyDown(receive(), { key: " " });
    });
    // Resuming a link it already has creates no access: it just runs, through the host.
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.Follow });
  });

  it("keeps the may-read arrow focusable when off, and never disabled", () => {
    draw(nas({ peerMayReadUs: false, peerModeTowardsUs: undefined }));

    expect(read()).toHaveAttribute("aria-disabled", "true");
    expect(read()).not.toBeDisabled();
    act(() => read().focus());
    expect(read()).toHaveFocus();
    fireEvent.keyDown(read(), { key: "Enter" });
    fireEvent.click(read());
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(recorded.actions.run).not.toHaveBeenCalled();
  });

  it("stops the other device reading, asking first", () => {
    draw(nas());

    fireEvent.click(read());
    expect(lastConfirmation()).toMatchObject({ title: "dataSync.arrow.read.stopTitle NAS" });
    void lastConfirmation().action();
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
    expect(screen.getAllByText("dataSync.mode.twoWay").length).toBeGreaterThan(0);
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
  it("say the same as the arrow and its badge", () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }));

    expect(screen.getByTestId("data-sync-mode-follow")).toBeChecked();
    expect(screen.getByTestId("data-sync-mode-badge")).toHaveAttribute("data-mode", "follow");
    expect(receive()).toHaveAttribute("aria-pressed", "true");
  });

  it("switch through the badge's menu exactly as through the buttons", async () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }));

    fireEvent.click(screen.getByTestId("data-sync-mode-badge"));
    const menu = screen.getByRole("menu");

    expect(within(menu).getByRole("menuitemradio", { checked: true })).toHaveTextContent(
      "dataSync.mode.follow",
    );
    // Both ways, towards a device that already reads this one: nothing is widened.
    await act(async () => {
      fireEvent.click(within(menu).getByRole("menuitemradio", { name: "dataSync.mode.twoWay" }));
    });
    expect(dataSyncApi.updateLink).toHaveBeenCalledWith(1, { mode: DataSyncLinkMode.TwoWay });
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
    const peer = syncPeerFromMapPeer(
      mapPeer("node-new", "New PC", {
        linkId: undefined,
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Off,
        state: undefined,
        receiving: false,
        peerMayRead: false,
      }),
    );

    draw(peer);
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

  it("copies once through a confirmation, then opens the review", async () => {
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

  it("says a copy once asked for access, and opens no review that cannot come yet", async () => {
    vi.mocked(dataSyncApi.links).mockResolvedValue([
      link(21, "node-nas", "NAS", {
        mode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.AwaitingAccess,
      }),
    ]);
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

describe("the badge's menu", () => {
  it("takes the keyboard on its chosen item, moves with the arrows and gives it back on Tab", () => {
    draw(nas({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }));
    const badge = screen.getByTestId("data-sync-mode-badge");

    act(() => badge.focus());
    fireEvent.keyDown(badge, { key: "ArrowDown" });
    const menu = screen.getByRole("menu");
    const [twoWay, follow] = within(menu).getAllByRole("menuitemradio");

    expect(menu).toHaveAccessibleName(badge.getAttribute("aria-label")!);
    expect(follow).toHaveFocus();
    expect(twoWay).toHaveAttribute("tabindex", "-1");
    fireEvent.keyDown(follow, { key: "ArrowDown" });
    expect(twoWay).toHaveFocus();
    fireEvent.keyDown(twoWay, { key: "End" });
    expect(follow).toHaveFocus();
    fireEvent.keyDown(follow, { key: "Tab" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(badge).toHaveFocus();
  });

  it("closes on Escape, and only itself: the details around it stay open", () => {
    const closeDetails = vi.fn();

    render(
      <MemoryRouter>
        <EscapableDetails onEscape={closeDetails}>
          <SyncRuleDrawing
            canManage
            actions={recorded.actions}
            initialWidth={520}
            now={NOW}
            peer={nas()}
            remoteAccessMode={RemoteAccessMode.Enabled}
            selfName="This PC"
            sharingEnabled={true}
          />
        </EscapableDetails>
      </MemoryRouter>,
    );
    const badge = screen.getByTestId("data-sync-mode-badge");

    fireEvent.click(badge);
    fireEvent.keyDown(within(screen.getByRole("menu")).getAllByRole("menuitemradio")[0], {
      key: "Escape",
    });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(badge).toHaveFocus();
    expect(closeDetails).not.toHaveBeenCalled();

    // With the menu closed, Escape is the details' again.
    fireEvent.keyDown(badge, { key: "Escape" });
    expect(closeDetails).toHaveBeenCalledTimes(1);
  });

  it("keeps a choice this window may not make focusable, and does not make it", () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Follow,
        lastMode: DataSyncLinkMode.Follow,
        peerMayReadUs: false,
        peerModeTowardsUs: undefined,
      }),
      { canManage: false },
    );

    fireEvent.click(screen.getByTestId("data-sync-mode-badge"));
    const twoWay = within(screen.getByRole("menu")).getByRole("menuitemradio", {
      name: "dataSync.mode.twoWay",
    });

    expect(twoWay).toHaveAttribute("aria-disabled", "true");
    expect(twoWay).not.toBeDisabled();
    fireEvent.click(twoWay);
    expect(recorded.actions.confirm).not.toHaveBeenCalled();
    expect(recorded.actions.run).not.toHaveBeenCalled();
  });
});

describe("what the rule editor keeps offering", () => {
  it("offers to stop the other device reading once receiving is off", async () => {
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.TwoWay,
        state: DataSyncLinkState.Stopped,
      }),
    );

    expect(screen.getByText("dataSync.off.stillReads NAS")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("data-sync-also-stop-reading"));
    expect(lastConfirmation()).toMatchObject({ title: "dataSync.arrow.read.stopTitle NAS" });
    await lastConfirmation().action();
    expect(dataSyncApi.revokeReader).toHaveBeenCalledWith("node-nas");
    cleanup();

    // Nothing to stop where it does not read this device.
    draw(
      nas({
        mode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.Stopped,
        peerMayReadUs: false,
        peerModeTowardsUs: undefined,
      }),
    );
    expect(screen.queryByTestId("data-sync-also-stop-reading")).toBeNull();
  });

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
    draw(nas({ state: DataSyncLinkState.Paused, pausedReason: 5, pausedDetail: "deletions=40" }));

    expect(screen.getByText("dataSync.status.paused.MassDeletion NAS 40")).toBeInTheDocument();
    expect(screen.getByText("dataSync.pause.applyAsUsual")).toBeInTheDocument();
    expect(screen.getByText("dataSync.pause.reviewDeletions")).toBeInTheDocument();
  });

  it("asks for access again when the other device revoked it, not when it turned sharing off", async () => {
    draw(nas({ state: DataSyncLinkState.AccessRevoked, lastErrorCode: "AccessRevoked" }));

    expect(screen.getByText("dataSync.status.AccessRevoked NAS")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("dataSync.pause.askAgain NAS"));
    });
    expect(dataSyncApi.resumeLink).toHaveBeenCalledWith(1, DataSyncResumeAction.AskAccessAgain);
    cleanup();

    // The same line for sharing turned off there: asking cannot help, so it is not offered.
    draw(nas({ state: DataSyncLinkState.PeerSharingOff, lastErrorCode: "PeerSharingOff" }));
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
      lastErrorCode: "Unreachable",
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

describe("the drawing's layout", () => {
  it("stands horizontally at 420 px and wider", () => {
    draw(nas(), { initialWidth: 420 });

    expect(screen.getByTestId("data-sync-rule-drawing")).toHaveAttribute(
      "data-orientation",
      "horizontal",
    );
  });

  it("stands vertically below 420 px, as its container is measured", () => {
    const measure = vi
      .spyOn(HTMLElement.prototype, "getBoundingClientRect")
      .mockImplementation(function (this: HTMLElement) {
        const width = this.dataset.testid === "data-sync-rule-drawing" ? 380 : 0;

        return {
          width,
          height: 300,
          top: 0,
          left: 0,
          right: width,
          bottom: 300,
          x: 0,
          y: 0,
        } as DOMRect;
      });

    try {
      draw(nas(), { initialWidth: 1000 });
      expect(screen.getByTestId("data-sync-rule-drawing")).toHaveAttribute(
        "data-orientation",
        "vertical",
      );
      // Vertical: the arrows point up and down.
      expect(receive().querySelector("[data-arrow]")).toHaveAttribute("data-arrow", "down");
      expect(read().querySelector("[data-arrow]")).toHaveAttribute("data-arrow", "up");
    } finally {
      measure.mockRestore();
    }
  });

  it("says both directions, as a list, to a screen reader", () => {
    draw(nas({ peerKinds: ["customProperty"] }));
    const items = within(screen.getByTestId("data-sync-rule-list")).getAllByRole("listitem");

    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent(
      "federation.map.direction.sync.in.active NAS, dataSync.mode.twoWay",
    );
    expect(items[1]).toHaveTextContent(
      "federation.map.direction.sync.out.active NAS: dataSync.kind.customProperty",
    );
  });
});
