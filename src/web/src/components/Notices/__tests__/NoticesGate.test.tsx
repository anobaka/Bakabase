import type { ReactNode } from "react";

import { act } from "@testing-library/react";
import { forwardRef } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import NoticesGate, { NOTICES_DISMISSED_SESSION_KEY } from "../NoticesGate";
import NoticesTopic from "../NoticesTopic";
import { useNoticeStore } from "../noticeStore";

import WhatsNewGate from "@/components/Changelog/WhatsNewGate";
import {
  GETTING_STARTED_FIRST_RUN_KEY,
  useFirstRunHelp,
} from "@/components/HelpCenter/useFirstRunHelp";
import { useStartupQueue } from "@/components/Startup/startupQueue";
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const api = vi.hoisted(() => ({
  getUiOptions: vi.fn(),
  markNoticesRead: vi.fn(),
  captureNoticeBaseline: vi.fn(),
  getAppInfo: vi.fn(),
  getChangelog: vi.fn(),
  navigate: vi.fn(),
  listManagedServers: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    options: {
      getUiOptions: api.getUiOptions,
      markNoticesRead: api.markNoticesRead,
      captureNoticeBaseline: api.captureNoticeBaseline,
    },
    app: { getAppInfo: api.getAppInfo },
    changelog: { getChangelog: api.getChangelog },
  },
}));
vi.mock("@/features/federation/serverApi", () => ({
  managedServerApi: { list: api.listManagedServers },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/bakaui", () => ({
  Button: forwardRef<
    HTMLButtonElement,
    {
      children?: ReactNode;
      onPress?: () => void;
      isDisabled?: boolean;
      "aria-label"?: string;
    }
  >(({ children, onPress, isDisabled, "aria-label": label }, ref) => (
    <button ref={ref} aria-label={label} disabled={isDisabled} type="button" onClick={onPress}>
      {children}
    </button>
  )),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Modal: ({
    title,
    children,
    footer,
    onClose,
  }: {
    title?: ReactNode;
    children?: ReactNode;
    footer?: ReactNode;
    onClose?: () => void;
  }) => (
    <div data-dialog="notices" role="dialog">
      <header>{title}</header>
      {children}
      <footer>{footer}</footer>
      <button type="button" onClick={onClose}>
        close dialog
      </button>
    </div>
  ),
}));
vi.mock("@/components/HelpCenter/HelpCenterModal", () => ({
  // A link inside the help center going to a page, as the real one handles it: the host's
  // onNavigate when it passes one, otherwise the page is opened and the help center closed.
  // (NoticesGuide.test.tsx drives the real help center.)
  default: ({
    topic,
    firstRun,
    onClose,
    onNavigate,
  }: {
    topic?: string;
    firstRun?: boolean;
    onClose: () => void;
    onNavigate?: (path: string) => void;
  }) => (
    <div data-dialog={firstRun ? "welcome" : `help:${topic}`} role="dialog">
      <button type="button" onClick={onClose}>
        close help
      </button>
      <button
        type="button"
        onClick={() => {
          if (onNavigate) {
            onNavigate("/federation");
          } else {
            window.location.hash = "/federation";
            onClose();
          }
        }}
      >
        link in help
      </button>
    </div>
  ),
}));
vi.mock("@/components/Changelog", () => ({
  ChangelogModal: ({
    version,
    from,
    onClose,
  }: {
    version: string;
    from: string;
    onClose: () => void;
  }) => (
    <div data-dialog={`changelog:${from}->${version}`} role="dialog">
      <button type="button" onClick={onClose}>
        close changelog
      </button>
    </div>
  ),
}));

const LAST_SEEN_KEY = "bakabase.changelog.lastSeenVersion";

/** The dashboard's welcome, as the dashboard hosts it. */
const Welcome = () => {
  const { showFirstRun, completeFirstRun } = useFirstRunHelp(
    GETTING_STARTED_FIRST_RUN_KEY,
    "gettingStarted",
  );

  return showFirstRun ? (
    <div data-dialog="welcome" role="dialog">
      <button type="button" onClick={completeFirstRun}>
        finish welcome
      </button>
    </div>
  ) : null;
};

let host: HTMLDivElement;
let root: Root;
let serverReadIds: string[];

const serverState = (extra: { baselinePending?: boolean; readIds?: string[] } = {}) => {
  serverReadIds = extra.readIds ?? [];
  api.getUiOptions.mockResolvedValue({
    code: 0,
    data: { notices: { readIds: serverReadIds, baselinePending: extra.baselinePending ?? false } },
  });
};

const viewer = (facts: Partial<ReturnType<typeof useRemoteAccessStore.getState>>) =>
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Enabled,
    ...facts,
  });

const renderStartup = async (withWelcome = false) => {
  await act(async () =>
    root.render(
      <>
        {withWelcome && <Welcome />}
        <NoticesGate />
        <WhatsNewGate />
      </>,
    ),
  );
  await settle();
};

const settle = async () => {
  for (let i = 0; i < 5; i++) await act(async () => await Promise.resolve());
};

const dialogs = () =>
  [...host.querySelectorAll("[role=dialog]")].map((node) => node.getAttribute("data-dialog"));

const noticeOnScreen = () =>
  host.querySelector("[data-notice-id]")?.getAttribute("data-notice-id") ?? null;

const click = async (text: string) => {
  const button = [...host.querySelectorAll("button")].find(
    (element) => element.textContent === text || element.getAttribute("aria-label") === text,
  );

  expect(button, text).toBeDefined();
  await act(async () => button!.click());
  await settle();
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  localStorage.clear();
  sessionStorage.clear();
  // The welcome is done and 2.3.0 was the last version this browser saw: an upgraded install.
  localStorage.setItem(GETTING_STARTED_FIRST_RUN_KEY, "true");
  localStorage.setItem(LAST_SEEN_KEY, "2.3.0");
  useStartupQueue.setState({ slots: {}, current: null });
  useNoticeStore.setState({ status: "idle", state: undefined, readHere: [], startupDone: false });
  useUiOptionsStore.setState({ data: {} as never, initialized: false });
  viewer({});
  serverState();
  // A desktop app that manages nothing: in particular, imported nothing from a thin client.
  api.listManagedServers.mockResolvedValue({ available: true, servers: [], requests: [] });
  api.markNoticesRead.mockImplementation(async (ids: string[]) => {
    serverReadIds = [...new Set([...serverReadIds, ...ids])];

    return { code: 0, data: { readIds: serverReadIds, baselinePending: false } };
  });
  api.captureNoticeBaseline.mockImplementation(async (ids: string[]) => ({
    code: 0,
    data: { readIds: ids, baselinePending: false },
  }));
  api.getAppInfo.mockResolvedValue({ code: 0, data: { coreVersion: "2.4.0" } });
  api.getChangelog.mockResolvedValue({ code: 0, data: { version: "2.4.0" } });
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
});

describe("startup notices", () => {
  it("shows unread notices one at a time, marks each read, then hands on to the release notes", async () => {
    // An id this build does not know is simply not one of its notices.
    serverState({ readIds: ["from-a-newer-build"] });
    await renderStartup();

    expect(dialogs()).toEqual(["notices"]);
    expect(noticeOnScreen()).toBe("multi-device");

    await click("notices.action.gotIt");
    expect(api.markNoticesRead).toHaveBeenLastCalledWith(["multi-device"]);
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
    // Nothing else opens while notices are left.
    expect(dialogs()).toEqual(["notices"]);
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.3.0");

    await click("notices.action.gotIt");
    expect(api.markNoticesRead).toHaveBeenLastCalledWith(["thin-client-discontinued"]);
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
    // Recorded once the notes are actually on screen.
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.4.0");
  });

  it("pages back and forth without marking anything", async () => {
    await renderStartup();

    await click("notices.dialog.next");
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
    // "Next" turned itself off; focus must not fall out of the dialog with it.
    expect(document.activeElement?.textContent).toBe("notices.action.gotIt");
    await click("notices.dialog.previous");
    expect(noticeOnScreen()).toBe("multi-device");
    expect(api.markNoticesRead).not.toHaveBeenCalled();
  });

  it("marks every unread notice read at once", async () => {
    await renderStartup();

    await click("notices.action.markAllRead");

    expect(api.markNoticesRead).toHaveBeenCalledOnce();
    expect(api.markNoticesRead).toHaveBeenCalledWith(["multi-device", "thin-client-discontinued"]);
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });

  it("offers no 'mark all' for a single notice", async () => {
    serverState({ readIds: ["multi-device"] });
    await renderStartup();

    expect(noticeOnScreen()).toBe("thin-client-discontinued");
    expect(host.textContent).not.toContain("notices.action.markAllRead");
    expect(host.textContent).not.toContain("notices.dialog.position");
  });

  it("waits for the welcome, and the release notes wait for the notices", async () => {
    localStorage.removeItem(GETTING_STARTED_FIRST_RUN_KEY);
    await renderStartup(true);

    expect(dialogs()).toEqual(["welcome"]);

    await click("finish welcome");
    expect(dialogs()).toEqual(["notices"]);

    await click("notices.action.markAllRead");
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });

  it("records a fresh install's baseline instead of greeting it with upgrade notes", async () => {
    serverState({ baselinePending: true });
    await renderStartup();

    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(
      ["multi-device", "thin-client-discontinued"],
      expect.anything(),
    );
    expect(api.markNoticesRead).not.toHaveBeenCalled();
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });

  it("keeps upgrade notes hidden when a fresh install's baseline could not be saved", async () => {
    serverState({ baselinePending: true });
    api.captureNoticeBaseline.mockRejectedValue(new Error("offline"));
    await renderStartup();

    expect(noticeOnScreen()).toBeNull();
  });

  it("neither asks nor shows anything in a window showing another server", async () => {
    viewer({ clientMode: ClientMode.PureClient });
    await renderStartup();

    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(api.captureNoticeBaseline).not.toHaveBeenCalled();
    expect(noticeOnScreen()).toBeNull();
    // Holds nothing up either.
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });

  it("neither asks nor shows anything in a browser that may only read this install", async () => {
    viewer({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Enabled,
    });
    await renderStartup();

    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(noticeOnScreen()).toBeNull();
  });

  it("shows a LAN administrator none of the desktop app's notices", async () => {
    viewer({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
    });
    await renderStartup();

    expect(api.getUiOptions).toHaveBeenCalledOnce();
    expect(noticeOnScreen()).toBeNull();
  });

  it("keeps notices closed unread for the next launch, not for the next page load", async () => {
    await renderStartup();

    await click("close dialog");
    expect(api.markNoticesRead).not.toHaveBeenCalled();
    expect(sessionStorage.getItem(NOTICES_DISMISSED_SESSION_KEY)).toBe("1");
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);

    // A reload in the same session.
    await act(async () => root.unmount());
    root = createRoot(host);
    useStartupQueue.setState({ slots: {}, current: null });
    useNoticeStore.setState({ status: "idle", state: undefined, readHere: [], startupDone: false });
    localStorage.setItem(LAST_SEEN_KEY, "2.4.0");
    await renderStartup();
    expect(dialogs()).toEqual([]);
  });

  it("marks a notice read when its guide is opened, then comes back to the rest", async () => {
    await renderStartup();

    await click("notices.item.multiDevice.action");
    expect(api.markNoticesRead).toHaveBeenCalledWith(["multi-device"]);
    expect(dialogs()).toEqual(["help:multiDevice"]);

    await click("close help");
    expect(dialogs()).toEqual(["notices"]);
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
  });

  it("goes where a notice points, and leaves the rest for the next launch", async () => {
    await renderStartup();

    await click("notices.dialog.next");
    await click("notices.item.thinClient.action");

    expect(api.markNoticesRead).toHaveBeenCalledWith(["thin-client-discontinued"]);
    expect(api.navigate).toHaveBeenCalledWith("/federation/devices?section=servers");
    // Neither the other notice nor the release notes open over the page the user asked for.
    expect(dialogs()).toEqual([]);
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.3.0");
  });

  it("follows what another window marks read", async () => {
    await renderStartup();
    expect(noticeOnScreen()).toBe("multi-device");

    // The server pushes the UI options to every window after a change.
    await act(async () =>
      useUiOptionsStore
        .getState()
        .update({ notices: { readIds: ["multi-device"], baselinePending: false } }),
    );
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
  });

  it("shows nothing and holds nothing up when the state cannot be read", async () => {
    api.getUiOptions.mockRejectedValue(new Error("offline"));
    await renderStartup();

    expect(noticeOnScreen()).toBeNull();
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });
});

describe("going to a page from the notices", () => {
  it("leaves the rest for the next launch when a link in a notice's guide is followed", async () => {
    await renderStartup();

    await click("notices.item.multiDevice.action");
    expect(dialogs()).toEqual(["help:multiDevice"]);

    await click("link in help");

    expect(api.navigate).toHaveBeenCalledWith("/federation");
    // Neither the other notice nor the release notes open over the page the user went to.
    expect(dialogs()).toEqual([]);
    expect(noticeOnScreen()).toBeNull();
    // The remaining notice is not read — it waits for the next launch, as after "close".
    expect(api.markNoticesRead).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem(NOTICES_DISMISSED_SESSION_KEY)).toBe("1");
    // Not recorded as seen: the notes are offered again next launch.
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.3.0");
  });
});

describe("the startup dialog takes its turn once", () => {
  it("does not open later when the help center's list retries a load that failed", async () => {
    api.getUiOptions.mockRejectedValueOnce(new Error("busy"));
    await renderStartup();
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
    await click("close changelog");
    expect(dialogs()).toEqual([]);

    // Later the user opens the help center at its Notices topic, which loads again.
    const manual = document.createElement("div");

    document.body.appendChild(manual);
    const manualRoot = createRoot(manual);

    try {
      await act(async () => manualRoot.render(<NoticesTopic onNavigate={vi.fn()} />));
      await settle();

      expect(api.getUiOptions).toHaveBeenCalledTimes(2);
      expect(useNoticeStore.getState().status).toBe("loaded");
      // The list shows what it learned...
      expect(manual.querySelectorAll("[data-notice-id]")).toHaveLength(2);
      // ...and the startup dialog stays where it was: done, and holding nothing up.
      expect(dialogs()).toEqual([]);
      expect(useStartupQueue.getState().slots.notices?.status).toBe("idle");
    } finally {
      await act(async () => manualRoot.unmount());
      manual.remove();
    }
  });

  it("does not open later when who is looking is only learned later", async () => {
    // The context could not be read at startup: nobody is taken for this install's window.
    viewer({ context: "unknown" });
    await renderStartup();

    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
    await click("close changelog");

    // A settings page reads the context again, and this time it answers.
    await act(async () => useRemoteAccessStore.setState({ context: "known" }));
    await settle();

    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(dialogs()).toEqual([]);
  });

  it("does not start over when the layout holding it is mounted again", async () => {
    api.getUiOptions.mockRejectedValueOnce(new Error("busy"));
    await renderStartup();
    await click("close changelog");
    // The help center's list loads the state after all.
    await act(async () => useNoticeStore.getState().load("local"));
    expect(useNoticeStore.getState().status).toBe("loaded");

    // A page outside the app's layout and back: the gate is a new component, the page load
    // is the same one.
    await act(async () => root.unmount());
    root = createRoot(host);
    await renderStartup();

    expect(noticeOnScreen()).toBeNull();
    expect(dialogs()).toEqual([]);
  });
});

describe("who is looking, when it is not known", () => {
  it("neither asks nor shows anything when the server could not say who is looking", async () => {
    // What the remote-access store is left with when its context request failed: initialized,
    // with defaults that read as this install's own window.
    viewer({ context: "unknown" });
    await renderStartup();

    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(noticeOnScreen()).toBeNull();
    // Holds nothing up.
    expect(dialogs()).toEqual(["changelog:2.3.0->2.4.0"]);
  });
});

describe("a fresh install that used the thin client", () => {
  const imported = {
    available: true,
    servers: [{ serverId: "nas", importedFromLegacyClient: true }],
    requests: [],
  };

  it("is shown the thin client's notice when its first start brought the pairings over", async () => {
    serverState({ baselinePending: true });
    api.listManagedServers.mockResolvedValue(imported);
    await renderStartup();

    // Only the multi-device notice goes into the baseline.
    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(["multi-device"], expect.anything());
    expect(dialogs()).toEqual(["notices"]);
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
    expect(host.textContent).not.toContain("notices.action.markAllRead");
  });

  it("is not, when it manages servers it paired itself", async () => {
    serverState({ baselinePending: true });
    api.listManagedServers.mockResolvedValue({
      ...imported,
      servers: [{ serverId: "nas", importedFromLegacyClient: false }],
    });
    await renderStartup();

    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(
      ["multi-device", "thin-client-discontinued"],
      expect.anything(),
    );
    expect(noticeOnScreen()).toBeNull();
  });

  it("keeps the notice upgrade-only when the pairings cannot be asked about", async () => {
    serverState({ baselinePending: true });
    api.listManagedServers.mockRejectedValue(new Error("ManagementUnavailable"));
    await renderStartup();

    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(
      ["multi-device", "thin-client-discontinued"],
      expect.anything(),
    );
    expect(noticeOnScreen()).toBeNull();
  });

  it("asks nothing of a browser on another device, and keeps the notice upgrade-only", async () => {
    viewer({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
    });
    serverState({ baselinePending: true });
    api.listManagedServers.mockResolvedValue(imported);
    await renderStartup();

    // Management is this install's own window's business (`/federation/local`).
    expect(api.listManagedServers).not.toHaveBeenCalled();
    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(
      ["multi-device", "thin-client-discontinued"],
      expect.anything(),
    );
  });

  it("changes nothing for an install that was upgraded", async () => {
    api.listManagedServers.mockResolvedValue(imported);
    await renderStartup();

    // No baseline to record, so nothing to ask.
    expect(api.listManagedServers).not.toHaveBeenCalled();
    expect(api.captureNoticeBaseline).not.toHaveBeenCalled();
    expect(noticeOnScreen()).toBe("multi-device");
  });
});
