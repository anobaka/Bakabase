import type { ReactNode } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { localToolServerUrl } from "./localTools";

import ToolExecutionNotice from "./index";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const mocks = vi.hoisted(() => ({ switchLocal: vi.fn() }));

vi.mock("@/features/federation/switching", () => ({ openLocalView: mocks.switchLocal }));
vi.mock("@/sdk/BApi", () => ({ default: {} }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Popover: ({ trigger, children }: { trigger: ReactNode; children: ReactNode }) => (
    <>
      {trigger}
      {children}
    </>
  ),
  Button: ({
    children,
    onPress,
    type,
    isLoading,
  }: {
    children: ReactNode;
    onPress?: () => void;
    type?: "submit";
    isLoading?: boolean;
  }) => (
    <button disabled={isLoading} type={type ?? "button"} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    errorMessage,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    errorMessage?: string;
  }) => (
    <>
      <label>
        {label}
        <input value={value} onChange={(event) => onValueChange(event.target.value)} />
      </label>
      {errorMessage && <div role="alert">{errorMessage}</div>}
    </>
  ),
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.switchLocal.mockResolvedValue(undefined);
  useRemoteAccessStore.setState({
    context: "known",
    initialized: true,
    isLocal: false,
    clientMode: ClientMode.RemoteBrowser,
    clientHost: undefined,
    serverName: "NAS",
  });
});

describe("tool execution context", () => {
  it("offers a navigation-only desktop link in a remote browser", () => {
    render(<ToolExecutionNotice tool="file-processor" />);
    expect(screen.getByText("NAS")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "toolExecution.openApp" })).toHaveAttribute(
      "href",
      "bakabase://tools/file-processor",
    );
    expect(mocks.switchLocal).not.toHaveBeenCalled();
  });
  it("keeps the notice for a loopback headless server", () => {
    useRemoteAccessStore.setState({ isLocal: true });
    render(<ToolExecutionNotice tool="file-name-modifier" />);
    expect(screen.getByRole("button", { name: "toolExecution.serverFiles" })).toBeInTheDocument();
    expect(screen.getByRole("link")).toHaveAttribute("href", "bakabase://tools/file-name-modifier");
  });
  it("does not treat unknown AllInOne defaults as confirmed local", () => {
    useRemoteAccessStore.setState({
      context: "unknown",
      initialized: false,
      isLocal: true,
      clientMode: ClientMode.AllInOne,
    });
    render(<ToolExecutionNotice tool="file-processor" />);
    expect(
      screen.getByRole("button", { name: "toolExecution.currentService" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link")).toBeInTheDocument();
    expect(screen.queryByText("NAS")).not.toBeInTheDocument();
  });
  it("leaves a local desktop informational rather than launching it again", () => {
    useRemoteAccessStore.setState({ isLocal: true, clientMode: ClientMode.AllInOne });
    render(<ToolExecutionNotice tool="file-processor" />);
    expect(
      screen.getByRole("button", { name: "toolExecution.currentService" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByText("toolExecution.localServer")).not.toBeInTheDocument();
  });
  it.each(["file-processor", "file-name-modifier"] as const)(
    "switches the desktop relay to the local %s route",
    async (tool) => {
      useRemoteAccessStore.setState({ clientMode: ClientMode.PureClient, clientHost: "console" });
      render(<ToolExecutionNotice tool={tool} />);
      fireEvent.click(screen.getByRole("button", { name: "toolExecution.switchLocal" }));
      await waitFor(() => expect(mocks.switchLocal).toHaveBeenCalledWith(`/${tool}`));
      expect(screen.queryByRole("link")).not.toBeInTheDocument();
      expect(screen.getByText("toolExecution.deviceMenu")).toBeInTheDocument();
    },
  );
  it("reports a failed relay switch and allows retry", async () => {
    useRemoteAccessStore.setState({ clientMode: ClientMode.PureClient, clientHost: "console" });
    mocks.switchLocal.mockRejectedValueOnce(new Error("offline"));
    render(<ToolExecutionNotice tool="file-processor" />);
    fireEvent.click(screen.getByRole("button", { name: "toolExecution.switchLocal" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("toolExecution.switchFailed");
    fireEvent.click(screen.getByRole("button", { name: "toolExecution.switchLocal" }));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    expect(mocks.switchLocal).toHaveBeenCalledTimes(2);
  });
  it("opens the entered local port on the same tool, without probing it", () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);

    render(<ToolExecutionNotice tool="file-name-modifier" />);
    fireEvent.change(screen.getByLabelText("toolExecution.address"), {
      target: { value: "http://localhost:42000" },
    });
    fireEvent.click(screen.getByRole("button", { name: "toolExecution.openServer" }));
    expect(open).toHaveBeenCalledWith(
      "http://localhost:42000/#/file-name-modifier",
      "_blank",
      "noopener,noreferrer",
    );
    open.mockRestore();
  });
  it("does not open an invalid local address", () => {
    const open = vi.spyOn(window, "open").mockReturnValue(null);

    render(<ToolExecutionNotice tool="file-processor" />);
    fireEvent.change(screen.getByLabelText("toolExecution.address"), {
      target: { value: "https://example.com" },
    });
    fireEvent.click(screen.getByRole("button", { name: "toolExecution.openServer" }));
    expect(screen.getByRole("alert")).toHaveTextContent("toolExecution.invalidAddress");
    expect(open).not.toHaveBeenCalled();
    open.mockRestore();
  });
});

describe("explicit local server addresses", () => {
  it.each(["http://localhost:5000", "https://127.0.0.1:34567/", "http://[::1]:4567"])(
    "accepts %s",
    (address) => {
      expect(localToolServerUrl(address, "file-processor")).toBe(
        `${new URL(address).origin}/#/file-processor`,
      );
    },
  );
  it.each([
    "",
    "localhost:1234",
    "javascript:alert(1)",
    "file:///tmp",
    "http://localhost.example.com",
    "http://user:secret@127.0.0.1",
    "http://127.0.0.1/path",
    "http://localhost/?token=secret",
    "http://localhost/#/resource",
    "http://192.168.3.23:34567",
  ])("rejects %s", (address) => {
    expect(localToolServerUrl(address, "file-processor")).toBeUndefined();
  });
});
