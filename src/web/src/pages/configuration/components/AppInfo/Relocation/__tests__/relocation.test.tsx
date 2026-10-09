import type { ReactNode } from "react";
import type * as ImportApi from "../../Import/api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { RelocationButton } from "..";

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  navigate: vi.fn(),
  success: vi.fn(),
  danger: vi.fn(),
  baseUrl: "",
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    request: mocks.request,
    get baseUrl() {
      return mocks.baseUrl;
    },
  },
}));
vi.mock("../../Import/api", async (importOriginal) => ({
  ...(await importOriginal<typeof ImportApi>()),
  navigateToAppDataSetup: mocks.navigate,
}));
vi.mock("@/components/bakaui", () => ({
  toast: { success: mocks.success, danger: mocks.danger },
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
  }) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({
    visible,
    title,
    children,
    footer,
  }: {
    visible: boolean;
    title: string;
    children: ReactNode;
    footer: ReactNode;
  }) =>
    visible ? (
      <div aria-label={title} role="dialog">
        {children}
        {footer}
      </div>
    ) : null,
  Snippet: ({ children }: { children: ReactNode }) => <code>{children}</code>,
}));
vi.mock("@/stores/remoteAccess", () => ({ useIsPureClient: () => false }));
vi.mock("@/stores/relocationPending", () => ({ useRelocationPendingStore: () => null }));

const key = (suffix: string) => `configuration.dataRelocation.${suffix}`;
const status = { supported: true, currentPath: "/old/appdata" };
const progress = (phase: ImportApi.AppDataImportPhase): ImportApi.AppDataImportProgress => ({
  id: "relocation-one",
  phase,
  completedFiles: 0,
  totalFiles: 4,
  completedBytes: 0,
  totalBytes: 1024,
  completedEntries: 0,
  totalEntries: 2,
  elapsedSeconds: 0,
  bytesPerSecond: 0,
  updatedAtUtc: "2026-10-08T12:00:00Z",
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.baseUrl = "";
  mocks.request.mockReset().mockResolvedValue({ code: 0, data: status });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("shared relocation maintenance", () => {
  it("checks capability and hides the entry when relocation is unavailable", async () => {
    mocks.request.mockResolvedValue({ code: 0, data: { ...status, supported: false } });
    render(<RelocationButton />);
    await waitFor(() => expect(mocks.request).toHaveBeenCalledOnce());
    expect(mocks.request.mock.calls[0][0]).toMatchObject({
      method: "GET",
      path: "/app/data-path/relocation",
    });
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("creates only a scoped session and navigates to the remote backend setup page", async () => {
    mocks.baseUrl = "https://backend.example/prefix?apiKey=secret";
    render(<RelocationButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockResolvedValueOnce({ code: 0, data: { setupToken: "move token" } });
    fireEvent.click(button);
    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledExactlyOnceWith(
        "https://backend.example/prefix/setup#setupToken=move%20token&lang=en",
      ),
    );
    expect(mocks.request.mock.calls.map(([request]) => [request.method, request.path])).toEqual([
      ["GET", "/app/data-path/relocation"],
      ["POST", "/app/data-path/relocation/setup-session"],
    ]);
    expect(mocks.request.mock.calls[1][0]).not.toHaveProperty("body");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it.each([
    [{ code: 403, message: "Management permission required" }, "Management permission required"],
    [{ code: 0, data: { setupToken: " " } }, key("setupFailed")],
  ])("keeps the current page when session authorization fails: %j", async (response, error) => {
    render(<RelocationButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockResolvedValueOnce(response);
    fireEvent.click(button);
    expect(await screen.findByRole("alert")).toHaveTextContent(error as string);
    expect(mocks.navigate).not.toHaveBeenCalled();
  });

  it("restores a queued relocation, shows its destination and cancels through its own endpoint", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        targetPath: "/new/appdata",
        progress: progress("queued"),
        monitorToken: "read-token",
      },
    });
    render(<RelocationButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("pending") }));
    expect(screen.getByText("/new/appdata")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toHaveAttribute(
      "href",
      `${window.location.origin}/app/data-path/import/progress#token=read-token&lang=en`,
    );
    expect(screen.queryByText("docker compose restart")).not.toBeInTheDocument();
    mocks.request.mockResolvedValueOnce({ code: 0 });
    fireEvent.click(screen.getByRole("button", { name: key("cancel") }));
    await screen.findByRole("button", { name: key("newImport") });
    expect(mocks.request.mock.calls.at(-1)?.[0]).toMatchObject({
      method: "DELETE",
      path: "/app/data-path/relocation",
    });
  });

  it("keeps the last queued state and disables cancellation while disconnected", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        targetPath: "/new/appdata",
        progress: progress("queued"),
        monitorToken: "read-token",
      },
    });
    render(<RelocationButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("pending") }));
    vi.useFakeTimers();
    mocks.request.mockRejectedValueOnce(new Error("Restarting"));
    // Re-render the summary to schedule its polling timer under fake timers.
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    fireEvent.click(screen.getByRole("button", { name: key("pending") }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(screen.getByRole("button", { name: key("cancel") })).toBeDisabled();
    expect(screen.getByText(key("reconnecting"))).toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("does not allow cancellation once copying starts", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: { ...status, progress: progress("copying") },
    });
    render(<RelocationButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("inProgress") }));
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
  });

  it("uses automatic maintenance messaging for queued relocation and cannot cancel it", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        targetPath: "/new/data",
        automaticMaintenance: true,
        progress: progress("queued"),
        monitorToken: "read-token",
      },
    });
    render(<RelocationButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("inProgress") }));
    expect(screen.getByText(key("automaticQueued"))).toBeInTheDocument();
    expect(screen.getByText("/new/data")).toBeInTheDocument();
    expect(screen.queryByText(key("restartInstructions"))).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toBeInTheDocument();
  });

  it("allows a new destination after completion without a stale queued path", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: { ...status, progress: progress("completed") },
    });
    render(<RelocationButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("viewResult") }));
    expect(screen.getByRole("button", { name: key("newImport") })).toBeEnabled();
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });
});
