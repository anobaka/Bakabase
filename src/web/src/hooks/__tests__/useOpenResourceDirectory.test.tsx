import { act, cleanup, fireEvent, render, renderHook, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useOpenResourceDirectory } from "../useOpenResourceDirectory";

import FolderLocationModal from "@/components/Resource/components/FolderLocationModal";
import { resourceFolderPath } from "@/components/Resource/resourceFolderPath";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const mocks = vi.hoisted(() => ({
  openResourceDirectory: vi.fn(),
  createPortal: vi.fn(),
  success: vi.fn(),
  danger: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { resource: { openResourceDirectory: mocks.openResourceDirectory } },
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title }: any) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  toast: { success: mocks.success, danger: mocks.danger },
}));

const initialRemote = useRemoteAccessStore.getState();
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
const copyCommandDescriptor = Object.getOwnPropertyDescriptor(document, "execCommand");
const resource = { id: 42, path: "/nas/media/title/video.mkv", isFile: true };

beforeEach(() => {
  vi.clearAllMocks();
  mocks.openResourceDirectory.mockResolvedValue({ code: 0 });
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
  });
});

afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
  vi.restoreAllMocks();
  if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
  else Reflect.deleteProperty(navigator, "clipboard");
  if (copyCommandDescriptor) Object.defineProperty(document, "execCommand", copyCommandDescriptor);
  else Reflect.deleteProperty(document, "execCommand");
});

describe("resource folder actions", () => {
  it.each([true, false])(
    "shows the folder location to a browser without calling a native action (local=%s)",
    (isLocal) => {
      useRemoteAccessStore.setState({ isLocal, clientMode: ClientMode.RemoteBrowser });
      const { result } = renderHook(useOpenResourceDirectory);

      expect(result.current.label).toBe("resource.folderLocation.title");
      act(() => result.current.open(resource));

      expect(mocks.openResourceDirectory).not.toHaveBeenCalled();
      expect(mocks.createPortal).toHaveBeenCalledWith(FolderLocationModal, {
        path: "/nas/media/title",
      });
    },
  );

  it.each([
    { clientMode: ClientMode.AllInOne, isLocal: true },
    { clientMode: ClientMode.PureClient, isLocal: false },
  ])("keeps native opening available for $clientMode", (context) => {
    useRemoteAccessStore.setState(context);
    const { result } = renderHook(useOpenResourceDirectory);

    expect(result.current.label).toBe("common.action.openFolder");
    act(() => result.current.open(resource));

    expect(mocks.openResourceDirectory).toHaveBeenCalledExactlyOnceWith({ id: 42 });
    expect(mocks.createPortal).not.toHaveBeenCalled();
  });

  it("does not launch a native action before caller context arrives", () => {
    useRemoteAccessStore.setState({ initialized: false });
    const { result } = renderHook(useOpenResourceDirectory);

    act(() => result.current.open(resource));
    expect(mocks.openResourceDirectory).not.toHaveBeenCalled();
  });

  it("uses the server's containing directory for a file and the resource path for a folder", () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.RemoteBrowser });
    const { result } = renderHook(useOpenResourceDirectory);

    act(() => result.current.open({ ...resource, directory: "/server/canonical-folder" }));
    expect(mocks.createPortal).toHaveBeenLastCalledWith(FolderLocationModal, {
      path: "/server/canonical-folder",
    });
    act(() => result.current.open({ ...resource, isFile: false, directory: "/nas/media/title" }));
    expect(mocks.createPortal).toHaveBeenLastCalledWith(FolderLocationModal, {
      path: resource.path,
    });
  });

  it("does nothing for a resource without local files", () => {
    const { result } = renderHook(useOpenResourceDirectory);

    act(() => result.current.open({ id: 42 }));
    expect(mocks.openResourceDirectory).not.toHaveBeenCalled();
    expect(mocks.createPortal).not.toHaveBeenCalled();
  });
});

describe("folder location paths", () => {
  it.each([
    ["/mnt/media/movie.mkv", "/mnt/media"],
    ["/movie.mkv", "/"],
    ["C:\\Movies\\movie.mkv", "C:\\Movies"],
    ["C:\\movie.mkv", "C:\\"],
    ["C:/movie.mkv", "C:/"],
    ["\\\\nas\\media\\movie.mkv", "\\\\nas\\media"],
    ["//nas/media/movie.mkv", "//nas/media"],
    ["/mnt/media/a\\b.mkv", "/mnt/media"],
    ["/mnt/a\\b/movie.mkv", "/mnt/a\\b"],
  ])("preserves the containing folder of %s", (path, expected) => {
    expect(resourceFolderPath(path, true)).toBe(expected);
  });

  it("leaves directory paths unchanged", () => {
    expect(resourceFolderPath("/nas/media/title", false)).toBe("/nas/media/title");
    expect(resourceFolderPath("C:\\", false)).toBe("C:\\");
  });
});

describe("copying a server folder location", () => {
  it("copies through the HTTP-compatible clipboard fallback", async () => {
    // Plain HTTP LAN origins do not expose navigator.clipboard.
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    let copied = "";
    const copy = vi.fn(() => {
      copied = [...document.querySelectorAll("textarea")].at(-1)!.value;

      return true;
    });

    Object.defineProperty(document, "execCommand", { configurable: true, value: copy });
    render(<FolderLocationModal path="/nas/media/title" />);
    expect(screen.getByLabelText("resource.folderLocation.serverPath")).toHaveValue(
      "/nas/media/title",
    );

    await act(async () => fireEvent.click(screen.getByText("resource.folderLocation.copyPath")));

    expect(copy).toHaveBeenCalledWith("copy");
    expect(copied).toBe("/nas/media/title");
    expect(mocks.success).toHaveBeenCalledWith("resource.folderLocation.copied");
    expect(document.querySelectorAll("textarea")).toHaveLength(1);
  });

  it("keeps the path available for manual copying when clipboard access fails", async () => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    Object.defineProperty(document, "execCommand", { configurable: true, value: () => false });
    render(<FolderLocationModal path="/nas/media/title" />);

    await act(async () => fireEvent.click(screen.getByText("resource.folderLocation.copyPath")));

    expect(mocks.danger).toHaveBeenCalledWith("resource.folderLocation.copyFailed");
    expect(mocks.success).not.toHaveBeenCalled();
    expect(screen.getByLabelText("resource.folderLocation.serverPath")).toHaveValue(
      "/nas/media/title",
    );
  });
});
