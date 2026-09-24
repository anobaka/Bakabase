import type { ReactElement, ReactNode } from "react";

import { act } from "@testing-library/react";
import { Children, forwardRef, isValidElement } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DashboardPage from "../index";

import WhatsNewGate from "@/components/Changelog/WhatsNewGate";
import { GETTING_STARTED_FIRST_RUN_KEY } from "@/components/HelpCenter/useFirstRunHelp";
import NoticesGate from "@/components/Notices/NoticesGate";
import { useNoticeStore } from "@/components/Notices/noticeStore";
import { useStartupQueue } from "@/components/Startup/startupQueue";
import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * The dashboard's first-run welcome is the real help center, hosted as the dashboard hosts
 * it, with the startup dialogs that come after it. A reader who follows a link out of the
 * welcome has gone somewhere: nothing that waited behind the welcome may open over that page.
 *
 * The case that makes it matter: a fresh desktop install whose first start brought over an
 * old thin client's pairings is shown the thin client's notice (it is left out of the fresh
 * install's baseline), so something does wait behind the welcome.
 */

const api = vi.hoisted(() => ({
  getUiOptions: vi.fn(),
  markNoticesRead: vi.fn(),
  captureNoticeBaseline: vi.fn(),
  getAppInfo: vi.fn(),
  getChangelog: vi.fn(),
  getDashboardOverview: vi.fn(),
  listManagedServers: vi.fn(),
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
    dashboard: { getDashboardOverview: api.getDashboardOverview },
  },
}));
vi.mock("@/features/federation/serverApi", () => ({
  managedServerApi: { list: api.listManagedServers },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/stores/pendingSearch", () => ({
  usePendingSearchStore: (selector: (store: unknown) => unknown) =>
    selector({ setPendingSearch: vi.fn() }),
}));
vi.mock("../components/RecentResources", () => ({ default: () => null }));
vi.mock("../components/ActivityOverview", () => ({ default: () => null }));
vi.mock("../components/DataMigrationHintModal", () => ({ DataMigrationHintModal: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: forwardRef<
    HTMLButtonElement,
    {
      children?: ReactNode;
      onPress?: () => void;
      isDisabled?: boolean;
      "aria-label"?: string;
      type?: "button" | "submit";
    }
  >(({ children, onPress, isDisabled, "aria-label": label, type }, ref) => (
    <button
      ref={ref}
      aria-label={label}
      disabled={isDisabled}
      type={type ?? "button"}
      onClick={onPress}
    >
      {children}
    </button>
  )),
  Card: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  CardBody: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Input: ({ "aria-label": label }: { "aria-label"?: string }) => <input aria-label={label} />,
  Tooltip: ({ children }: { children?: ReactNode }) => <>{children}</>,
  Modal: ({
    visible,
    title,
    children,
    footer,
    onClose,
  }: {
    visible?: boolean;
    title?: ReactNode;
    children?: ReactNode;
    footer?: ReactNode;
    onClose?: () => void;
  }) =>
    visible ? (
      <div role="dialog">
        <header data-title>{title}</header>
        {children}
        <footer>{footer}</footer>
        <button type="button" onClick={onClose}>
          close dialog
        </button>
      </div>
    ) : null,
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

let host: HTMLDivElement;
let root: Root;

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

/** The dashboard as `BasicLayout` hosts it: the page, then the startup gates. */
const renderDashboard = async () => {
  await act(async () =>
    root.render(
      <>
        <DashboardPage />
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
  // A browser that has never opened the app: the welcome is due, no version was seen before.
  localStorage.clear();
  sessionStorage.clear();
  window.location.hash = "";
  useStartupQueue.setState({ slots: {}, current: null });
  useNoticeStore.setState({ status: "idle", state: undefined, readHere: [], startupDone: false });
  useUiOptionsStore.setState({ data: {} as never, initialized: false });
  // This install's own desktop window.
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Enabled,
  });
  // A fresh install, whose first start brought over an old thin client's pairing.
  api.getUiOptions.mockResolvedValue({
    code: 0,
    data: { notices: { readIds: [], baselinePending: true } },
  });
  api.captureNoticeBaseline.mockImplementation(async (ids: string[]) => ({
    code: 0,
    data: { readIds: ids, baselinePending: false },
  }));
  api.markNoticesRead.mockImplementation(async (ids: string[]) => ({
    code: 0,
    data: { readIds: ["multi-device", ...ids], baselinePending: false },
  }));
  api.listManagedServers.mockResolvedValue({
    available: true,
    servers: [{ serverId: "nas", importedFromLegacyClient: true }],
    requests: [],
  });
  api.getAppInfo.mockResolvedValue({ code: 0, data: { coreVersion: "2.4.0" } });
  api.getChangelog.mockResolvedValue({ code: 0, data: { version: "2.4.0" } });
  api.getDashboardOverview.mockResolvedValue({ code: 500 });
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("the dashboard's first-run welcome", () => {
  it("puts off the rest when the reader follows one of its links to a page", async () => {
    await renderDashboard();
    expect(dialogs()).toEqual(["helpCenter.title"]);
    // The thin client's notice waits behind it: left out of the fresh install's baseline.
    expect(api.captureNoticeBaseline).toHaveBeenCalledWith(["multi-device"], expect.anything());

    // The multi-device overview, and its link to the multi-device library.
    await click("helpCenter.topic.multiDevice");
    await click("helpCenter.multiDevice.open.library");

    // Nothing that waited opens over the page the reader went to.
    expect(dialogs()).toEqual([]);
    expect(noticeOnScreen()).toBeNull();
    expect(api.navigate).toHaveBeenCalledWith("/federation");
    // The welcome is done; the notice is not read, so the next launch shows it.
    expect(localStorage.getItem(GETTING_STARTED_FIRST_RUN_KEY)).toBe("true");
    expect(api.markNoticesRead).not.toHaveBeenCalled();
    // Went through the app's router, as the notices' own links do.
    expect(window.location.hash).toBe("");
  });

  it("puts off the rest from a notice's own link in the help center's list of notices", async () => {
    await renderDashboard();

    await click("helpCenter.topic.notices");
    await click("notices.item.thinClient.action");

    expect(dialogs()).toEqual([]);
    expect(api.navigate).toHaveBeenCalledWith("/federation/devices?section=servers");
    expect(localStorage.getItem(GETTING_STARTED_FIRST_RUN_KEY)).toBe("true");
  });

  it("hands on to the notice when the welcome is closed without going anywhere", async () => {
    await renderDashboard();

    await click("helpCenter.action.getStarted");

    expect(api.navigate).not.toHaveBeenCalled();
    expect(dialogs()).toEqual(["notices.dialog.title"]);
    expect(noticeOnScreen()).toBe("thin-client-discontinued");
    expect(localStorage.getItem(GETTING_STARTED_FIRST_RUN_KEY)).toBe("true");
  });
});
