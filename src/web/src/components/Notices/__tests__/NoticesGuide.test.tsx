import type { ReactElement, ReactNode } from "react";

import { act } from "@testing-library/react";
import { Children, forwardRef, isValidElement } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import NoticesGate, { NOTICES_DISMISSED_SESSION_KEY } from "../NoticesGate";
import { useNoticeStore } from "../noticeStore";

import WhatsNewGate from "@/components/Changelog/WhatsNewGate";
import { useStartupQueue } from "@/components/Startup/startupQueue";
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * The guide a notice opens is the real help center, with its real topics: whatever link the
 * reader follows in it — in the topic the notice opened, or in any topic they moved on to —
 * must put off what is left of the startup dialogs. NoticesGate.test.tsx covers the rest of
 * the gate with the help center stubbed.
 */

const api = vi.hoisted(() => ({
  getUiOptions: vi.fn(),
  markNoticesRead: vi.fn(),
  captureNoticeBaseline: vi.fn(),
  getAppInfo: vi.fn(),
  getChangelog: vi.fn(),
  navigate: vi.fn(),
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
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/bakaui", () => ({
  Button: forwardRef<
    HTMLButtonElement,
    { children?: ReactNode; onPress?: () => void; isDisabled?: boolean; "aria-label"?: string }
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
    <div role="dialog">
      <header data-title>{title}</header>
      {children}
      <footer>{footer}</footer>
      <button type="button" onClick={onClose}>
        close dialog
      </button>
    </div>
  ),
  Tab: () => null,
  Tabs: ({
    children,
    selectedKey,
    onSelectionChange,
  }: {
    children: ReactNode;
    selectedKey: string;
    onSelectionChange: (key: string) => void;
  }) => (
    <div role="tablist">
      {Children.toArray(children)
        .filter(isValidElement)
        .map((child) => {
          const key = String((child as ReactElement).key).replace(/^\.\$/, "");

          return (
            <button
              key={key}
              aria-selected={key === selectedKey}
              role="tab"
              type="button"
              onClick={() => onSelectionChange(key)}
            >
              {(child as ReactElement<{ title: ReactNode }>).props.title}
            </button>
          );
        })}
    </div>
  ),
}));
vi.mock("@/components/Changelog", () => ({
  ChangelogModal: ({ version, from }: { version: string; from: string }) => (
    <div role="dialog">
      <header data-title>{`changelog:${from}->${version}`}</header>
    </div>
  ),
}));

const LAST_SEEN_KEY = "bakabase.changelog.lastSeenVersion";

let host: HTMLDivElement;
let root: Root;
let serverReadIds: string[];

const settle = async () => {
  for (let i = 0; i < 5; i++) await act(async () => await Promise.resolve());
};

/** Each dialog on screen, by its title. */
const dialogs = () =>
  [...host.querySelectorAll("[role=dialog] > [data-title]")].map((node) => node.textContent);

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

const renderStartup = async () => {
  await act(async () =>
    root.render(
      <>
        <NoticesGate />
        <WhatsNewGate />
      </>,
    ),
  );
  await settle();
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  // The help center scrolls its selected entry into view; jsdom lays nothing out.
  HTMLElement.prototype.scrollIntoView = vi.fn();
  localStorage.clear();
  sessionStorage.clear();
  window.location.hash = "";
  // An upgraded install, 2.3.0 → 2.4.0, both notices unread, in this install's own window.
  localStorage.setItem(LAST_SEEN_KEY, "2.3.0");
  useStartupQueue.setState({ slots: {}, current: null });
  useNoticeStore.setState({ status: "idle", state: undefined, readHere: [], startupDone: false });
  useUiOptionsStore.setState({ data: {} as never, initialized: false });
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Enabled,
  });
  serverReadIds = [];
  api.getUiOptions.mockResolvedValue({
    code: 0,
    data: { notices: { readIds: [], baselinePending: false } },
  });
  api.markNoticesRead.mockImplementation(async (ids: string[]) => {
    serverReadIds = [...new Set([...serverReadIds, ...ids])];

    return { code: 0, data: { readIds: serverReadIds, baselinePending: false } };
  });
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

describe("the guide a notice opens", () => {
  it("puts off the rest when the reader follows one of its links to a page", async () => {
    await renderStartup();
    await click("notices.item.multiDevice.action");
    expect(dialogs()).toEqual(["helpCenter.title"]);

    // The multi-device overview's link to the multi-device library.
    await click("helpCenter.multiDevice.open.library");

    expect(api.navigate).toHaveBeenCalledWith("/federation");
    // Neither the other notice nor the release notes open over the page the reader went to.
    expect(dialogs()).toEqual([]);
    expect(noticeOnScreen()).toBeNull();
    // The one read is the one whose guide this was; the other waits for the next launch.
    expect(serverReadIds).toEqual(["multi-device"]);
    expect(sessionStorage.getItem(NOTICES_DISMISSED_SESSION_KEY)).toBe("1");
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.3.0");
    // Went through the app's router, as a notice's own route does.
    expect(window.location.hash).toBe("");
  });

  it("puts off the rest from a topic the reader moved on to", async () => {
    await renderStartup();
    await click("notices.item.multiDevice.action");

    // The help center's own list of notices, and the thin client notice's action there.
    await click("helpCenter.topic.notices");
    await click("notices.item.thinClient.action");

    expect(api.navigate).toHaveBeenCalledWith("/federation/devices?section=servers");
    expect(dialogs()).toEqual([]);
    expect(serverReadIds).toEqual(["multi-device", "thin-client-discontinued"]);
    expect(localStorage.getItem(LAST_SEEN_KEY)).toBe("2.3.0");
  });

  it("comes back to the rest when it is closed without going anywhere", async () => {
    await renderStartup();
    await click("notices.item.multiDevice.action");

    await click("close dialog");

    expect(api.navigate).not.toHaveBeenCalled();
    expect(dialogs()).toEqual(["notices.dialog.title"]);
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
  });
});
