import type { ReactNode } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import FilePathValue from "./index";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { useDeploymentPathsStore } from "@/stores/deploymentPaths";

const mocks = vi.hoisted(() => ({ load: vi.fn(), open: vi.fn(), copy: vi.fn() }));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: { label?: string }) =>
      values?.label ? `${key}:${values.label}` : key,
  }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: { getDeploymentPaths: mocks.load },
    tool: { openFileOrDirectory: mocks.open },
  },
}));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: mocks.copy }));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    "aria-label": label,
  }: {
    children: ReactNode;
    onPress?: () => void;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} onClick={onPress}>
      {children}
    </button>
  ),
}));

const container = (
  storageKind = "bind",
  hostPath: string | undefined = "/Users/test/Bakabase/logs",
) => ({
  code: 0,
  data: { isContainer: true, paths: [{ serverPath: "/data/logs", hostPath, storageKind }] },
});

describe("FilePathValue", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useRemoteAccessStore.setState({ initialized: true, clientMode: ClientMode.RemoteBrowser });
    useDeploymentPathsStore.setState({ loaded: false, loading: false, data: undefined });
    mocks.load.mockResolvedValue(container());
    mocks.copy.mockResolvedValue(undefined);
  });

  it("shows and copies host and container paths separately without a native open action", async () => {
    render(<FilePathValue path="/data/logs" />);
    await screen.findByText("/Users/test/Bakabase/logs");
    expect(screen.queryByRole("button", { name: "filePath.openFolder" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "filePath.copy:filePath.hostPath" }));
    await waitFor(() => expect(mocks.copy).toHaveBeenLastCalledWith("/Users/test/Bakabase/logs"));
    fireEvent.click(screen.getByText("filePath.containerPath"));
    fireEvent.click(screen.getByRole("button", { name: "filePath.copy:filePath.containerPath" }));
    await waitFor(() => expect(mocks.copy).toHaveBeenLastCalledWith("/data/logs"));
    expect(mocks.open).not.toHaveBeenCalled();
  });

  it("preserves all-in-one opening of the original local path without requesting deployment metadata", () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.AllInOne });
    render(<FilePathValue path={"C:\\Bakabase\\logs"} />);
    fireEvent.click(screen.getByRole("button", { name: "filePath.openFolder" }));
    expect(mocks.open).toHaveBeenCalledWith({ path: "C:\\Bakabase\\logs" });
    expect(mocks.load).not.toHaveBeenCalled();
  });

  it("never treats a Docker host path as the desktop relay's local path", async () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.PureClient });
    render(<FilePathValue path="/data/logs" />);
    await screen.findByText("/Users/test/Bakabase/logs");
    fireEvent.click(screen.getByRole("button", { name: "filePath.openFolder" }));
    expect(mocks.open).toHaveBeenCalledWith({ path: "/data/logs" });
    expect(mocks.open).not.toHaveBeenCalledWith({ path: "/Users/test/Bakabase/logs" });
  });

  it.each(["volume", "container", "unknown"])(
    "does not invent a host path for %s storage",
    async (kind) => {
      mocks.load.mockResolvedValue({
        code: 0,
        data: { isContainer: true, paths: [{ serverPath: "/data/logs", storageKind: kind }] },
      });
      render(<FilePathValue path="/data/logs" />);
      await screen.findByText("filePath.containerPath");
      expect(screen.getByText("/data/logs")).toBeInTheDocument();
      expect(screen.queryByText("filePath.hostPath")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "filePath.openFolder" })).not.toBeInTheDocument();
    },
  );

  it("falls back to a copyable server path when an older server lacks metadata", async () => {
    mocks.load.mockRejectedValue(new Error("404"));
    render(<FilePathValue path="/data/logs" />);
    await waitFor(() => expect(useDeploymentPathsStore.getState().loading).toBe(false));
    expect(screen.getByText("filePath.serverPath")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "filePath.copy:filePath.serverPath" }));
    await waitFor(() => expect(mocks.copy).toHaveBeenCalledWith("/data/logs"));
    expect(mocks.load).toHaveBeenCalledWith({ showErrorToast: false });
  });

  it("retries a transient metadata failure when the path view is opened again", async () => {
    mocks.load.mockRejectedValueOnce(new Error("Server restarting"));
    const first = render(<FilePathValue path="/data/logs" />);

    await waitFor(() => expect(useDeploymentPathsStore.getState().loading).toBe(false));
    first.unmount();
    render(<FilePathValue path="/data/logs" />);
    await screen.findByText("/Users/test/Bakabase/logs");
    expect(mocks.load).toHaveBeenCalledTimes(2);
  });

  it("loads metadata once for multiple path rows and reports copying failure honestly", async () => {
    mocks.copy.mockRejectedValue(new Error("Clipboard unavailable"));
    render(
      <>
        <FilePathValue path="/data/logs" />
        <FilePathValue path="/data/backups" />
      </>,
    );
    await screen.findByText("/Users/test/Bakabase/logs");
    expect(mocks.load).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: "filePath.copy:filePath.hostPath" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("filePath.copyFailed");
    expect(screen.queryByRole("button", { name: /^filePath.copied/ })).not.toBeInTheDocument();
  });
});
