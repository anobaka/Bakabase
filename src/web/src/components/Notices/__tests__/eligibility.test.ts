import type { NoticeDefinition } from "../registry";

import { AiOutlineCluster } from "react-icons/ai";
import { beforeEach, describe, expect, it, vi } from "vitest";

import {
  freshInstallFactsAsked,
  noticeViewerOf,
  pendingNotices,
  toNoticeState,
  upgradeOnlyNoticeIds,
} from "../eligibility";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const remoteAccessApi = vi.hoisted(() => ({ getRemoteAccessContext: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { remoteAccess: remoteAccessApi } }));

const notice = (id: string, extra: Partial<NoticeDefinition> = {}): NoticeDefinition => ({
  id,
  introducedIn: "2.4.0",
  order: 0,
  icon: AiOutlineCluster,
  titleKey: `t.${id}`,
  bodyKey: `b.${id}`,
  ...extra,
});

type ViewerFacts = Parameters<typeof noticeViewerOf>[0];

const facts = (extra: Partial<ViewerFacts> = {}): ViewerFacts => ({
  initialized: true,
  context: "known",
  isLocal: true,
  clientMode: ClientMode.AllInOne,
  mode: RemoteAccessMode.Enabled,
  ...extra,
});

describe("noticeViewerOf", () => {
  it("waits until the server has said who is looking", () => {
    expect(noticeViewerOf(facts({ initialized: false }))).toBeUndefined();
  });

  it("is this install's own window when the browser sits at it", () => {
    expect(noticeViewerOf(facts())).toBe("local");
  });

  it("is nobody when the server could not say who is looking", () => {
    // The remote-access store's defaults after a failed request read as the local window.
    expect(noticeViewerOf(facts({ context: "unknown" }))).toBeNull();
    expect(
      noticeViewerOf(facts({ context: "unknown", mode: RemoteAccessMode.Unrestricted })),
    ).toBeNull();
  });

  it("never engages in a window showing another server, however it looks", () => {
    // The console relay (and the retired thin client) answer as PureClient — and a relay
    // is on this machine's loopback, so isLocal alone would be wrong.
    expect(noticeViewerOf(facts({ clientMode: ClientMode.PureClient }))).toBeNull();
    expect(
      noticeViewerOf(
        facts({
          clientMode: ClientMode.PureClient,
          isLocal: false,
          mode: RemoteAccessMode.Unrestricted,
        }),
      ),
    ).toBeNull();
  });

  it("lets a LAN browser in only where it may change this install", () => {
    const remote = { isLocal: false, clientMode: ClientMode.RemoteBrowser };

    expect(noticeViewerOf(facts({ ...remote, mode: RemoteAccessMode.Unrestricted }))).toBe(
      "lanAdmin",
    );
    expect(noticeViewerOf(facts({ ...remote, mode: RemoteAccessMode.Enabled }))).toBeNull();
    expect(noticeViewerOf(facts({ ...remote, mode: RemoteAccessMode.Disabled }))).toBeNull();
  });
});

describe("pendingNotices", () => {
  const registry = [
    notice("later", { order: 20 }),
    notice("first", { order: 10 }),
    notice("for-servers", { order: 30, audience: ["lanAdmin", "local"] }),
    notice("upgrade", { order: 40, upgradeOnly: true }),
  ];

  it("lists unread notices for the viewer in reading order", () => {
    const pending = pendingNotices(registry, { readIds: [], baselinePending: false }, "local");

    expect(pending.map((item) => item.id)).toEqual(["first", "later", "for-servers", "upgrade"]);
  });

  it("drops read notices and tolerates ids it does not know", () => {
    const pending = pendingNotices(
      registry,
      { readIds: ["first", "from-a-newer-build", ""], baselinePending: false },
      "local",
    );

    expect(pending.map((item) => item.id)).toEqual(["later", "for-servers", "upgrade"]);
  });

  it("shows a LAN administrator only the notices meant for one", () => {
    const pending = pendingNotices(registry, { readIds: [], baselinePending: false }, "lanAdmin");

    expect(pending.map((item) => item.id)).toEqual(["for-servers"]);
  });

  it("hides upgrade-only notices while a fresh install has not recorded its baseline", () => {
    const pending = pendingNotices(registry, { readIds: [], baselinePending: true }, "local");

    expect(pending.map((item) => item.id)).toEqual(["first", "later", "for-servers"]);
  });

  it("names the upgrade-only notices a fresh install records", () => {
    expect(upgradeOnlyNoticeIds(registry)).toEqual(["upgrade"]);
  });
});

describe("a fresh install's baseline", () => {
  const registry = [
    notice("upgrade", { upgradeOnly: true }),
    notice("thin", { upgradeOnly: true, showOnFreshInstallWhen: "thinClientPairingsImported" }),
    // Not upgrade-only: nothing to leave out of a baseline, so nothing to ask.
    notice("always", { showOnFreshInstallWhen: "thinClientPairingsImported" }),
  ];

  it("asks only what an upgrade-only notice depends on", () => {
    expect(freshInstallFactsAsked(registry)).toEqual(["thinClientPairingsImported"]);
    expect(freshInstallFactsAsked([registry[0]!, registry[2]!])).toEqual([]);
  });

  it("leaves out a notice whose fact holds, and only then", () => {
    expect(upgradeOnlyNoticeIds(registry, { thinClientPairingsImported: true })).toEqual([
      "upgrade",
    ]);
    expect(upgradeOnlyNoticeIds(registry, { thinClientPairingsImported: false })).toEqual([
      "upgrade",
      "thin",
    ]);
    // Unknown is not "holds".
    expect(upgradeOnlyNoticeIds(registry, {})).toEqual(["upgrade", "thin"]);
  });
});

describe("who is looking, from the remote-access store", () => {
  beforeEach(() => {
    remoteAccessApi.getRemoteAccessContext.mockReset();
    // As the page starts.
    useRemoteAccessStore.setState({
      initialized: false,
      context: "asking",
      isLocal: true,
      clientMode: ClientMode.AllInOne,
      mode: RemoteAccessMode.Disabled,
    });
  });

  it("waits while the context is being asked for", () => {
    expect(noticeViewerOf(useRemoteAccessStore.getState())).toBeUndefined();
  });

  it("is nobody after the context request failed, though the store reads as local", async () => {
    remoteAccessApi.getRemoteAccessContext.mockRejectedValue(new Error("502"));

    await useRemoteAccessStore.getState().load();
    const state = useRemoteAccessStore.getState();

    // What the rest of the app keeps working with...
    expect([state.initialized, state.isLocal, state.clientMode]).toEqual([
      true,
      true,
      ClientMode.AllInOne,
    ]);
    // ...is not taken as this install's own window.
    expect(noticeViewerOf(state)).toBeNull();
  });

  it("is nobody after an answer that carries no context", async () => {
    remoteAccessApi.getRemoteAccessContext.mockResolvedValue({ code: 500, message: "busy" });

    await useRemoteAccessStore.getState().load();

    // Settled for notices, rather than keeping everything after them in the startup order
    // waiting; the rest of the store is as it was.
    expect(useRemoteAccessStore.getState().context).toBe("unknown");
    expect(useRemoteAccessStore.getState().initialized).toBe(false);
    expect(noticeViewerOf(useRemoteAccessStore.getState())).toBeNull();
  });

  it("keeps an answer it had when asking again fails", async () => {
    remoteAccessApi.getRemoteAccessContext.mockResolvedValueOnce({
      code: 0,
      data: { isLocal: true, clientMode: ClientMode.AllInOne, mode: RemoteAccessMode.Enabled },
    });
    await useRemoteAccessStore.getState().load();
    remoteAccessApi.getRemoteAccessContext.mockRejectedValueOnce(new Error("502"));
    await useRemoteAccessStore.getState().load();

    expect(noticeViewerOf(useRemoteAccessStore.getState())).toBe("local");
  });

  it("is the server's answer once it has one", async () => {
    remoteAccessApi.getRemoteAccessContext.mockResolvedValue({
      code: 0,
      data: {
        isLocal: false,
        clientMode: ClientMode.RemoteBrowser,
        mode: RemoteAccessMode.Unrestricted,
      },
    });

    await useRemoteAccessStore.getState().load();

    expect(noticeViewerOf(useRemoteAccessStore.getState())).toBe("lanAdmin");
  });
});

describe("toNoticeState", () => {
  it("reads a server that predates notices as an install with nothing read", () => {
    expect(toNoticeState(undefined)).toEqual({ readIds: [], baselinePending: false });
    expect(toNoticeState(null)).toEqual({ readIds: [], baselinePending: false });
    expect(toNoticeState({ readIds: null })).toEqual({ readIds: [], baselinePending: false });
  });

  it("keeps only string ids", () => {
    expect(
      toNoticeState({ readIds: ["a", 1 as unknown as string, "b"], baselinePending: true }),
    ).toEqual({ readIds: ["a", "b"], baselinePending: true });
  });
});
