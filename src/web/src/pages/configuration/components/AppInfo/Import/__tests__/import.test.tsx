import type { ReactNode } from "react";
import type * as ImportApi from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AppDataImportButton from "..";
import { getAppDataImportMonitorUrl, getAppDataImportSetupUrl } from "../api";

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
vi.mock("../api", async (importOriginal) => ({
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
}));

const key = (suffix: string) => `configuration.dataImport.${suffix}`;
const status = { supported: true, currentPath: "/app/data" };
const progress = (
  phase: ImportApi.AppDataImportProgress["phase"],
  overrides: Partial<ImportApi.AppDataImportProgress> = {},
): ImportApi.AppDataImportProgress => ({
  id: "import-one",
  phase,
  completedFiles: 0,
  totalFiles: 20,
  completedBytes: 0,
  totalBytes: 2048,
  completedEntries: 0,
  totalEntries: 4,
  elapsedSeconds: 0,
  bytesPerSecond: 0,
  updatedAtUtc: "2026-10-08T12:00:00Z",
  ...overrides,
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.request.mockReset();
  mocks.baseUrl = "";
  mocks.request.mockResolvedValue({ code: 0, data: status });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("shared setup entry", () => {
  it("waits for an explicit server capability and hides the entry on unsupported hosts", async () => {
    mocks.request.mockResolvedValue({ code: 0, data: { ...status, supported: false } });
    render(<AppDataImportButton />);

    await waitFor(() => expect(mocks.request).toHaveBeenCalledOnce());
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("hides the entry when an older or unavailable backend cannot answer", async () => {
    mocks.request.mockRejectedValue(new Error("404"));
    render(<AppDataImportButton />);

    await waitFor(() => expect(mocks.request).toHaveBeenCalledOnce());
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("authorizes a setup session and navigates without choosing a source or queueing an import", async () => {
    render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockResolvedValueOnce({ code: 0, data: { setupToken: "scoped-setup-token" } });
    fireEvent.click(button);

    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledExactlyOnceWith(
        `${window.location.origin}/setup#setupToken=scoped-setup-token&lang=en`,
      ),
    );
    expect(mocks.request.mock.calls.map(([request]) => [request.method, request.path])).toEqual([
      ["GET", "/app/data-path/import"],
      ["POST", "/app/data-path/import/setup-session"],
    ]);
    expect(mocks.request.mock.calls[1][0]).not.toHaveProperty("body");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it.each([
    [{ code: 403, message: "Management permission required" }, "Management permission required"],
    [{ code: 0 }, "configuration.dataImport.setupFailed"],
    [{ code: 0, data: { setupToken: " " } }, "configuration.dataImport.setupFailed"],
  ])(
    "keeps the user on the current page when session creation is rejected: %j",
    async (response, error) => {
      render(<AppDataImportButton />);
      const button = await screen.findByRole("button", { name: key("button") });

      mocks.request.mockResolvedValueOnce(response);
      fireEvent.click(button);

      expect(await screen.findByRole("alert")).toHaveTextContent(error as string);
      expect(mocks.navigate).not.toHaveBeenCalled();
      expect(screen.getByText(key("setupIntro"))).toBeInTheDocument();
      expect(screen.getByRole("button", { name: key("openSetup") })).toBeEnabled();
      expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    },
  );

  it("can retry after a transport failure", async () => {
    render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockRejectedValueOnce(new Error("Network unavailable"));
    fireEvent.click(button);
    expect(await screen.findByRole("alert")).toHaveTextContent("Network unavailable");
    expect(mocks.navigate).not.toHaveBeenCalled();
    mocks.request.mockResolvedValueOnce({ code: 0, data: { setupToken: "retry-token" } });
    fireEvent.click(screen.getByRole("button", { name: key("openSetup") }));
    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledExactlyOnceWith(
        getAppDataImportSetupUrl("retry-token", "en"),
      ),
    );
  });

  it("disables duplicate clicks while authorization is in flight", async () => {
    render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });
    let finish!: (response: unknown) => void;

    mocks.request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    fireEvent.click(button);
    fireEvent.click(button);
    expect(button).toBeDisabled();
    expect(mocks.request).toHaveBeenCalledTimes(2);
    await act(async () => finish({ code: 0, data: { setupToken: "one-session" } }));
    expect(mocks.navigate).toHaveBeenCalledOnce();
  });

  it("does not navigate after the settings component has been left", async () => {
    const view = render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });
    let finish!: (response: unknown) => void;

    mocks.request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    fireEvent.click(button);
    view.unmount();
    await act(async () => finish({ code: 0, data: { setupToken: "late-session" } }));
    expect(mocks.navigate).not.toHaveBeenCalled();
  });

  it("uses the actual backend URL and sends neither API credentials nor setup tokens in the query", () => {
    mocks.baseUrl = "https://user:secret@example.com/server/?apiKey=private#old";
    const url = new URL(getAppDataImportSetupUrl("scoped/setup&token", "zh-CN"));

    expect(url.origin).toBe("https://example.com");
    expect(url.pathname).toBe("/server/setup");
    expect(url.username).toBe("");
    expect(url.password).toBe("");
    expect(url.search).toBe("");
    expect(url.hash).toBe("#setupToken=scoped%2Fsetup%26token&lang=cn");
    const monitor = new URL(getAppDataImportMonitorUrl("read/only&token", "en"));

    expect(monitor.pathname).toBe("/server/app/data-path/import/progress");
    expect(monitor.search).toBe("");
    expect(monitor.hash).toBe("#token=read%2Fonly%26token&lang=en");
  });

  it("navigates to a remote backend's setup page instead of the frontend origin", async () => {
    mocks.baseUrl = "http://server.example:34567";
    render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockResolvedValueOnce({ code: 0, data: { setupToken: "remote-session" } });
    fireEvent.click(button);
    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledExactlyOnceWith(
        "http://server.example:34567/setup#setupToken=remote-session&lang=en",
      ),
    );
  });

  it("opens an independently hosted setup URL supplied by the desktop coordinator", async () => {
    render(<AppDataImportButton />);
    const button = await screen.findByRole("button", { name: key("button") });

    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: { setupToken: "parent-session", setupUrl: "http://127.0.0.1:41234/setup" },
    });
    fireEvent.click(button);
    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledExactlyOnceWith(
        "http://127.0.0.1:41234/setup#setupToken=parent-session&lang=en",
      ),
    );
    expect(() => getAppDataImportSetupUrl("token", "en", "javascript:alert(1)")).toThrow();
  });

  it("keeps a proxy prefix for server-relative setup and monitor routes", () => {
    mocks.baseUrl = "https://backend.example/proxy/";
    expect(getAppDataImportSetupUrl("setup-token", "en", "/setup")).toBe(
      "https://backend.example/proxy/setup#setupToken=setup-token&lang=en",
    );
    expect(getAppDataImportMonitorUrl("read-token", "cn", "/app/data-path/import/progress")).toBe(
      "https://backend.example/proxy/app/data-path/import/progress#token=read-token&lang=cn",
    );
    const monitor = new URL(
      getAppDataImportMonitorUrl(
        "read-token",
        "en",
        "http://user:secret@127.0.0.1:41234/app/data-path/import/progress?key=private",
      ),
    );

    expect(monitor.origin).toBe("http://127.0.0.1:41234");
    expect(monitor.username).toBe("");
    expect(monitor.password).toBe("");
    expect(monitor.search).toBe("");
    expect(() => getAppDataImportMonitorUrl("token", "en", "javascript:alert(1)")).toThrow();
  });

  it("shows managed queued work without legacy restart or cancel actions", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        automaticMaintenance: true,
        progress: progress("queued"),
        monitorToken: "parent-monitor",
        monitorUrl: "http://127.0.0.1:41234/app/data-path/import/progress",
      },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("inProgress") }));
    expect(screen.getByText(key("automaticPreparing"))).toBeInTheDocument();
    expect(screen.getByText(key("automaticQueued"))).toBeInTheDocument();
    expect(screen.getByText(key("automaticMonitorHelp"))).toBeInTheDocument();
    expect(screen.queryByText(key("phase.queued"))).not.toBeInTheDocument();
    expect(screen.queryByText(key("restartInstructions"))).not.toBeInTheDocument();
    expect(screen.queryByText("docker compose restart")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toHaveAttribute(
      "href",
      "http://127.0.0.1:41234/app/data-path/import/progress#token=parent-monitor&lang=en",
    );
    expect(mocks.request).toHaveBeenCalledOnce();
  });

  it("explains a managed failure without offering a new operation or cancellation", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        automaticMaintenance: true,
        progress: progress("failed", { error: "Worker exited" }),
        monitorToken: "parent-monitor",
      },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("failedNotice") }));
    expect(screen.getByRole("alert")).toHaveTextContent("Worker exited");
    expect(screen.getByText(key("automaticFailure"))).toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("newImport") })).not.toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("keeps queued-import controls and the read-only progress link without recreating a session", async () => {
    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        progress: progress("queued"),
        monitorToken: "read-only-token",
      },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("pending") }));
    const link = screen.getByRole("link", { name: key("openMonitor") });

    expect(link).toHaveAttribute("href", getAppDataImportMonitorUrl("read-only-token", "en"));
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(screen.getByText("docker compose restart")).toBeInTheDocument();
    expect(mocks.request).toHaveBeenCalledOnce();
    expect(mocks.navigate).not.toHaveBeenCalled();

    mocks.request.mockRejectedValueOnce(new Error("Network unavailable"));
    fireEvent.click(screen.getByRole("button", { name: key("cancel") }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Network unavailable");
    expect(screen.getByRole("button", { name: key("cancel") })).toBeEnabled();

    mocks.request.mockResolvedValueOnce({ code: 0 });
    fireEvent.click(screen.getByRole("button", { name: key("cancel") }));
    expect(await screen.findByText(key("phase.cancelled"))).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: key("newImport") })).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("shows a combined import's source, new destination and retained directory, and cancels it as an import", async () => {
    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        targetPath: "/server/new-library",
        progress: progress("queued", {
          sourcePath: "/import",
          targetPath: "/server/new-library",
          backupPath: status.currentPath,
        }),
        monitorToken: "combined-monitor",
      },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("pending") }));

    expect(screen.getByText(key("sourcePath")).nextElementSibling).toHaveTextContent("/import");
    expect(screen.getByText(key("targetPath")).nextElementSibling).toHaveTextContent(
      "/server/new-library",
    );
    expect(screen.getByText(key("retainedDataPath")).nextElementSibling).toHaveTextContent(
      status.currentPath,
    );
    expect(screen.getByText(key("retainedDataHelp"))).toBeInTheDocument();
    expect(screen.getByText(key("restartBodyNewDirectory"))).toBeInTheDocument();
    expect(screen.queryByText(key("restartBody"))).not.toBeInTheDocument();
    expect(screen.queryByText(key("backupPath"))).not.toBeInTheDocument();

    mocks.request.mockResolvedValueOnce({ code: 0 });
    fireEvent.click(screen.getByRole("button", { name: key("cancel") }));
    await screen.findByText(key("phase.cancelled"));
    expect(mocks.request.mock.calls[mocks.request.mock.calls.length - 1][0]).toMatchObject({
      method: "DELETE",
      path: "/app/data-path/import",
    });
  });

  it("restores combined-import locations from progress after the pending plan is cleared", async () => {
    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: {
        ...status,
        currentPath: "/server/new-library",
        progress: progress("completed", {
          sourcePath: "/import",
          targetPath: "/server/new-library",
          backupPath: "/server/previous-library",
        }),
      },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("viewResult") }));

    expect(screen.getByText(key("targetPath")).nextElementSibling).toHaveTextContent(
      "/server/new-library",
    );
    expect(screen.getByText(key("sourcePath")).nextElementSibling).toHaveTextContent("/import");
    expect(screen.getByText(key("retainedDataPath")).nextElementSibling).toHaveTextContent(
      "/server/previous-library",
    );
    expect(screen.queryByText(key("backupPath"))).not.toBeInTheDocument();
    expect(screen.queryByText(key("restartInstructions"))).not.toBeInTheDocument();
  });
});

describe("import progress polling", () => {
  const showQueuedImport = async () => {
    vi.useFakeTimers();
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        progress: progress("queued"),
        monitorToken: "monitor-token",
      },
    });
    let view!: ReturnType<typeof render>;

    await act(async () => {
      view = render(<AppDataImportButton />);
    });
    fireEvent.click(screen.getByRole("button", { name: key("pending") }));

    return view;
  };
  const poll = () => act(async () => vi.advanceTimersByTimeAsync(3000));

  it("retains a managed stopping state and its independent monitor through a business outage", async () => {
    vi.useFakeTimers();
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        automaticMaintenance: true,
        progress: progress("stopping", { elapsedSeconds: 70 }),
        monitorToken: "parent-monitor",
        monitorUrl: "http://127.0.0.1:41234/app/data-path/import/progress",
      },
    });
    await act(async () => {
      render(<AppDataImportButton />);
    });
    fireEvent.click(screen.getByRole("button", { name: key("inProgress") }));
    expect(screen.getByText(key("phase.stopping"))).toBeInTheDocument();
    expect(screen.getByText(key("automaticStopping"))).toBeInTheDocument();
    expect(screen.getByText(key("stoppingLongRunning"))).toBeInTheDocument();
    expect(screen.queryByText(key("longRunning"))).not.toBeInTheDocument();
    mocks.request.mockRejectedValueOnce(new Error("Business stopped"));
    await poll();
    expect(screen.getByText(key("reconnecting"))).toBeInTheDocument();
    expect(screen.getByText(key("phase.stopping"))).toBeInTheDocument();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toHaveAttribute(
      "href",
      "http://127.0.0.1:41234/app/data-path/import/progress#token=parent-monitor&lang=en",
    );
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("retains the last known state through a restart and only toasts after explicit completion", async () => {
    await showQueuedImport();
    mocks.request.mockRejectedValueOnce(new Error("Connection refused"));
    await poll();

    expect(screen.getByText(key("reconnecting"))).toBeInTheDocument();
    expect(screen.getByText(key("phase.queued"))).toBeInTheDocument();
    expect(screen.getByRole("button", { name: key("cancel") })).toBeDisabled();
    expect(screen.getByRole("link", { name: key("openMonitor") })).toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();

    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        progress: progress("copying", {
          completedFiles: 10,
          completedBytes: 1024,
          elapsedSeconds: 65,
          bytesPerSecond: 512,
          remainingSeconds: 2,
        }),
        monitorToken: "monitor-token",
      },
    });
    await poll();
    expect(screen.queryByText(key("reconnecting"))).not.toBeInTheDocument();
    expect(screen.getByText(key("phase.copying"))).toBeInTheDocument();
    expect(screen.getByText(key("longRunning"))).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("value", "1024");
    expect(screen.getByRole("progressbar")).toHaveAttribute("max", "2048");
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();

    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: { ...status, progress: progress("starting") },
    });
    await poll();
    expect(screen.getByText(key("phase.starting"))).toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();

    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        progress: progress("completed", { backupPath: "/app/data/backups/appdata-imports/id" }),
      },
    });
    await poll();
    expect(screen.getByText(key("phase.completed"))).toBeInTheDocument();
    expect(screen.getByText(key("backupPath"))).toBeInTheDocument();
    expect(screen.queryByText(key("retainedDataPath"))).not.toBeInTheDocument();
    expect(screen.getByText("/app/data/backups/appdata-imports/id")).toBeInTheDocument();
    expect(screen.queryByText(key("restartInstructions"))).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("pending") })).not.toBeInTheDocument();
    expect(mocks.success).toHaveBeenCalledExactlyOnceWith(key("completedNotice"));
    await poll();
    expect(mocks.success).toHaveBeenCalledOnce();

    mocks.request.mockResolvedValueOnce({ code: 0, data: { setupToken: "next-import" } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: key("newImport") }));
    });
    expect(mocks.navigate).toHaveBeenCalledWith(getAppDataImportSetupUrl("next-import", "en"));
  });

  it("surfaces failure once and does not offer cancellation after copying has begun", async () => {
    await showQueuedImport();
    mocks.request.mockResolvedValue({
      code: 0,
      data: {
        ...status,
        sourcePath: "/import",
        progress: progress("failed", { error: "Disk is full" }),
        monitorToken: "monitor-token",
      },
    });
    await poll();

    expect(screen.getByRole("alert")).toHaveTextContent("Disk is full");
    expect(screen.queryByRole("button", { name: key("cancel") })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: key("confirm") })).not.toBeInTheDocument();
    expect(mocks.danger).toHaveBeenCalledExactlyOnceWith({
      title: key("failedNotice"),
      description: "Disk is full",
    });
    await poll();
    expect(mocks.danger).toHaveBeenCalledOnce();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("does not infer success when a pending marker merely disappears", async () => {
    await showQueuedImport();
    mocks.request.mockResolvedValue({ code: 0, data: status });
    await poll();

    expect(screen.queryByRole("button", { name: key("pending") })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: key("openSetup") })).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("does not repeat a historical completion toast when opening settings", async () => {
    mocks.request.mockResolvedValue({
      code: 0,
      data: { ...status, progress: progress("completed") },
    });
    render(<AppDataImportButton />);
    fireEvent.click(await screen.findByRole("button", { name: key("viewResult") }));

    expect(screen.getByText(key("completedNotice"))).toBeInTheDocument();
    expect(mocks.success).not.toHaveBeenCalled();
  });

  it("avoids overlapping polls and cancels its in-flight request when unmounted", async () => {
    const view = await showQueuedImport();
    let finish!: (value: unknown) => void;

    mocks.request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await poll();
    const signal = mocks.request.mock.calls[1][0].signal as AbortSignal;

    await act(async () => vi.advanceTimersByTimeAsync(9000));
    expect(mocks.request).toHaveBeenCalledTimes(2);
    view.unmount();
    expect(signal.aborted).toBe(true);
    await act(async () => finish({ code: 0, data: status }));
    await act(async () => vi.advanceTimersByTimeAsync(30_000));
    expect(mocks.request).toHaveBeenCalledTimes(2);
  });
});
