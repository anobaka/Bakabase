import React, { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileSystemEntryIcon from "..";

import BApi from "@/sdk/BApi";
import { ClientMode, IconType, RuntimeMode } from "@/sdk/constants";
import { useAppContextStore } from "@/stores/appContext";
import { useIconsStore } from "@/stores/icons";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const { getIconData } = vi.hoisted(() => ({ getIconData: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { file: { getIconData } } }));
vi.mock("@/sdk/Api", () => ({ Api: class {} }));
vi.mock("@/components/utils", () => ({
  splitPathIntoSegments: (path: string) => path.split(/[\\/]/),
}));
vi.mock("i18next", () => ({ default: { t: (key: string) => key } }));

const iconData = "data:image/png;base64,dGVzdA==";
const mounted = new Set<() => Promise<void>>();

const render = async (element: React.ReactNode) => {
  const container = document.createElement("div");

  document.body.appendChild(container);
  const root = createRoot(container);
  const rerender = async (next: React.ReactNode) => {
    await act(async () => root.render(next));
  };
  const unmount = async () => {
    await act(async () => root.unmount());
    container.remove();
    mounted.delete(unmount);
  };

  mounted.add(unmount);
  await rerender(element);

  return { container, rerender, unmount };
};

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  getIconData.mockReset().mockResolvedValue({ code: 0, data: null });
  useIconsStore.setState({ icons: {} });
  useRemoteAccessStore.setState({
    initialized: true,
    isLocal: true,
    clientMode: ClientMode.AllInOne,
  });
  useAppContextStore.setState({ bApi2: BApi, runtimeMode: RuntimeMode.WinForms });
});

afterEach(async () => {
  for (const unmount of mounted) await unmount();
});

describe("filesystem icons", () => {
  it("waits for caller capabilities and keeps remote browsers on the default icon", async () => {
    useRemoteAccessStore.setState({ initialized: false });
    const { container } = await render(<FileSystemEntryIcon size={20} type={IconType.Directory} />);

    expect(getIconData).not.toHaveBeenCalled();
    await act(async () => {
      useRemoteAccessStore.setState({
        initialized: true,
        isLocal: false,
        clientMode: ClientMode.RemoteBrowser,
      });
    });

    expect(getIconData).not.toHaveBeenCalled();
    expect(container.querySelector("svg")).not.toBeNull();
    expect(container.querySelector("img")).toBeNull();
  });

  it("does not assume a local headless server has native icons before AppContext arrives", async () => {
    useAppContextStore.setState({ bApi2: null, runtimeMode: RuntimeMode.Dev });
    const { container } = await render(<FileSystemEntryIcon size={20} type={IconType.Directory} />);

    expect(getIconData).not.toHaveBeenCalled();
    await act(async () => {
      useAppContextStore.setState({ bApi2: BApi, runtimeMode: RuntimeMode.Docker });
    });

    expect(getIconData).not.toHaveBeenCalled();
    expect(container.querySelector("svg")).not.toBeNull();
  });

  it("loads desktop icons once their runtime context is available", async () => {
    useAppContextStore.setState({ bApi2: null });
    getIconData.mockResolvedValue({ code: 0, data: iconData });
    const { container } = await render(<FileSystemEntryIcon size={20} type={IconType.Directory} />);

    expect(getIconData).not.toHaveBeenCalled();
    await act(async () => {
      useAppContextStore.setState({ bApi2: BApi, runtimeMode: RuntimeMode.MacOS });
    });

    expect(container.querySelector("img")?.src).toBe(iconData);
    expect(getIconData).toHaveBeenCalledTimes(1);
  });

  it("keeps the relay's local icon handler available when the managed server is headless", async () => {
    useRemoteAccessStore.setState({ isLocal: false, clientMode: ClientMode.PureClient });
    useAppContextStore.setState({ bApi2: null, runtimeMode: RuntimeMode.Docker });
    getIconData.mockResolvedValue({ code: 0, data: iconData });
    const { container } = await render(<FileSystemEntryIcon size={20} type={IconType.Directory} />);

    expect(container.querySelector("img")?.src).toBe(iconData);
    expect(getIconData).toHaveBeenCalledTimes(1);
  });

  it("shares concurrent extension lookups, including StrictMode effect replays", async () => {
    let resolveIcon!: (value: { data: string }) => void;

    getIconData.mockReturnValue(new Promise((resolve) => (resolveIcon = resolve)));
    const { container } = await render(
      <StrictMode>
        <FileSystemEntryIcon path="/one.pdf" size={20} type={IconType.Dynamic} />
        <FileSystemEntryIcon path="/two.pdf" size={20} type={IconType.Dynamic} />
      </StrictMode>,
    );

    expect(getIconData).toHaveBeenCalledTimes(1);
    await act(async () => resolveIcon({ data: iconData }));

    expect(container.querySelectorAll("img")).toHaveLength(2);
    await render(<FileSystemEntryIcon path="/three.pdf" size={20} type={IconType.Dynamic} />);
    expect(getIconData).toHaveBeenCalledTimes(1);
  });

  it.each(["empty", "rejected"])(
    "caches an %s lookup as unavailable without retrying on mount",
    async (result) => {
      if (result === "rejected")
        getIconData.mockRejectedValue(new Error("Native icon unavailable"));
      const first = await render(<FileSystemEntryIcon size={20} type={IconType.UnknownFile} />);

      expect(useIconsStore.getState().icons[`${IconType.UnknownFile}-`]).toBeNull();
      expect(first.container.querySelector("svg")).not.toBeNull();
      await first.unmount();
      await render(<FileSystemEntryIcon size={20} type={IconType.UnknownFile} />);

      expect(getIconData).toHaveBeenCalledTimes(1);
    },
  );

  it("does not render an old path's delayed icon after the entry changes", async () => {
    let resolveOldIcon!: (value: { data: string }) => void;

    getIconData.mockReturnValueOnce(new Promise((resolve) => (resolveOldIcon = resolve)));
    getIconData.mockResolvedValue({ data: iconData });
    const { container, rerender } = await render(
      <FileSystemEntryIcon disableCache path="/old.exe" size={20} type={IconType.Dynamic} />,
    );

    await rerender(
      <FileSystemEntryIcon disableCache path="/new.exe" size={20} type={IconType.Dynamic} />,
    );
    expect(container.querySelector("img")?.src).toBe(iconData);
    await act(async () => resolveOldIcon({ data: "data:image/png;base64,b2xk" }));

    expect(container.querySelector("img")?.src).toBe(iconData);
    expect(getIconData).toHaveBeenCalledTimes(2);
    expect(useIconsStore.getState().icons).toEqual({});
  });
});
