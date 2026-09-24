import type { ReactNode } from "react";

import { act } from "@testing-library/react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import NoticesTopic from "../NoticesTopic";
import { useNoticeStore } from "../noticeStore";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useUiOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const api = vi.hoisted(() => ({
  getUiOptions: vi.fn(),
  markNoticesRead: vi.fn(),
  captureNoticeBaseline: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { options: api } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children?: ReactNode; onPress?: () => void }) => (
    <button type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span data-chip>{children}</span>,
}));

let host: HTMLDivElement;
let root: Root;
const onNavigate = vi.fn();
const onOpenTopic = vi.fn();

const render = async () => {
  await act(async () =>
    root.render(<NoticesTopic onNavigate={onNavigate} onOpenTopic={onOpenTopic} />),
  );
  for (let i = 0; i < 3; i++) await act(async () => await Promise.resolve());
};

const listed = () =>
  [...host.querySelectorAll("[data-notice-id]")].map((node) => node.getAttribute("data-notice-id"));

const chips = () => [...host.querySelectorAll("[data-chip]")].map((node) => node.textContent);

const click = async (text: string) => {
  const button = [...host.querySelectorAll("button")].find((b) => b.textContent === text);

  expect(button, text).toBeDefined();
  await act(async () => button!.click());
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  useNoticeStore.setState({ status: "idle", state: undefined, readHere: [], startupDone: false });
  useUiOptionsStore.setState({ data: {} as never, initialized: false });
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Enabled,
  });
  api.getUiOptions.mockResolvedValue({
    code: 0,
    data: { notices: { readIds: ["multi-device"], baselinePending: false } },
  });
  api.markNoticesRead.mockImplementation(async (ids: string[]) => ({
    code: 0,
    data: { readIds: ["multi-device", ...ids], baselinePending: false },
  }));
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
});

describe("the help center's notices", () => {
  it("lists every notice, newest first, with what has been read", async () => {
    await render();

    expect(listed()).toEqual(["thin-client-discontinued", "multi-device"]);
    expect(chips()).toEqual(["notices.state.unread", "notices.state.read"]);
  });

  it("marks one read from the list", async () => {
    await render();

    await click("notices.action.gotIt");

    expect(api.markNoticesRead).toHaveBeenCalledWith(["thin-client-discontinued"]);
    expect(chips()).toEqual(["notices.state.read", "notices.state.read"]);
  });

  it("follows a notice's action inside the help center or to its page", async () => {
    await render();

    await click("notices.item.multiDevice.action");
    expect(onOpenTopic).toHaveBeenCalledWith({ topic: "multiDevice", section: undefined });

    await click("notices.item.thinClient.action");
    expect(onNavigate).toHaveBeenCalledWith("/federation/devices?section=servers");
  });

  it("only shows the text in a window showing another server", async () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.PureClient });
    await render();

    expect(listed()).toEqual(["thin-client-discontinued", "multi-device"]);
    expect(api.getUiOptions).not.toHaveBeenCalled();
    expect(chips()).toEqual([]);
    expect(host.textContent).toContain("notices.topic.readOnly");
    expect(host.textContent).not.toContain("notices.item.thinClient.action");
  });
});
