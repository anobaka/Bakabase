import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Run the actual embedded maintenance page, including polling and reconnect behavior.
const html = readFileSync(
  resolve(__dirname, "../../../apps/Bakabase.Service/Components/ServerData/ImportProgress.html"),
  "utf8",
);
const token = "a".repeat(64);
const page = "/bakabase/app/data-path/import/progress";
const element = <T extends HTMLElement = HTMLElement>(id: string) =>
  document.getElementById(id) as T;
const snapshot = (phase: string, extra: Record<string, unknown> = {}) => ({
  id: "test-operation",
  operation: "import",
  phase,
  startedAtUtc: "2026-10-08T00:00:00Z",
  updatedAtUtc: new Date().toISOString(),
  completedBytes: 1024,
  totalBytes: 2048,
  completedFiles: 1,
  totalFiles: 2,
  completedEntries: 0,
  totalEntries: 0,
  elapsedSeconds: 65,
  bytesPerSecond: 512,
  remainingSeconds: 2,
  currentFile: "covers/current.jpg",
  backupPath: "/data/backups/appdata-imports/test-operation",
  ...extra,
});
const response = (data: unknown, status = 200) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => data }) as Response;
const nextPoll = () => vi.advanceTimersByTimeAsync(1500);

async function mountPage(responder: (options: RequestInit) => Promise<Response> | Response) {
  const fetchMock = vi.fn((_path: string, options: RequestInit) => responder(options));

  vi.stubGlobal("fetch", fetchMock);
  const parsed = new DOMParser().parseFromString(html, "text/html");

  document.body.innerHTML = parsed.body.innerHTML;
  window.eval(parsed.querySelector("script")!.textContent!);
  await vi.advanceTimersByTimeAsync(0);

  return fetchMock;
}
beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-08T00:01:05Z"));
  sessionStorage.clear();
  history.replaceState({}, "", `${page}#token=${token}&lang=en`);
});
afterEach(() => {
  vi.clearAllTimers();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  document.body.replaceChildren();
});

describe("maintenance progress page", () => {
  it("uses a preparation step for first-run imports that already execute without a restart", async () => {
    history.replaceState({}, "", `${page}#token=${token}&lang=cn`);
    await mountPage(() => response(snapshot("scanning")));
    expect(element("timeline").children[0]).toHaveTextContent("准备");
    expect(element("timeline")).not.toHaveTextContent("等待重启");
    expect(element("phase")).toHaveTextContent("正在扫描来源文件");
    expect(element("intro")).not.toHaveTextContent("手动重新启动");
  });
  it("shows queued work as a manual restart, with a clear stage route and read-only polling", async () => {
    const fetchMock = await mountPage(() => response(snapshot("queued")));

    expect(element("timeline").children).toHaveLength(7);
    expect(element("timeline").querySelector('[aria-current="step"]')).toHaveTextContent("Prepare");
    expect(element("intro")).toHaveTextContent("copying has not started");
    expect(element("explanation")).toHaveTextContent(
      "does not stop or restart the server automatically",
    );
    expect(element("explanation")).toHaveTextContent("docker compose restart");
    expect(element("return")).toBeVisible();
    expect(element("bar")).not.toBeVisible();
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(
      `${page}/status`,
      expect.objectContaining({
        headers: { "X-Bakabase-Import-Token": token },
        credentials: "omit",
        cache: "no-store",
      }),
    );
    expect(fetchMock.mock.calls[0][1].method).toBeUndefined();
  });
  it("keeps large-file metrics visible without interpreting 100% as ready", async () => {
    let state = snapshot("copying");
    const fetchMock = await mountPage(() => response(state));

    expect(element("percent")).toHaveTextContent("50.0%");
    expect(element("size")).toHaveTextContent("1.0 KiB / 2.0 KiB");
    expect(element("files")).toHaveTextContent("1 / 2");
    expect(element("speed")).toHaveTextContent("512 B/s");
    expect(element("elapsed")).toHaveTextContent("0:01:05");
    expect(element("file")).toHaveTextContent("covers/current.jpg");
    expect(element("eta")).toHaveTextContent("excludes checks and database upgrades");
    expect(element("return")).not.toBeVisible();
    state = snapshot("copying", { completedBytes: 2048, completedFiles: 2 });
    await nextPoll();
    expect(element("percent")).toHaveTextContent("100.0%");
    expect(element("phase")).toHaveTextContent("Copying files");
    expect(element("explanation")).toHaveTextContent("100% still requires checks and startup");
    expect(element("return")).not.toBeVisible();
    state = snapshot("verifying");
    await nextPoll();
    expect(element("phase")).toHaveTextContent("Checking data integrity");
    state = snapshot("starting");
    await nextPoll();
    expect(element("explanation")).toHaveTextContent("does not mean the server is ready");
    expect(element("return")).not.toBeVisible();
    state = snapshot("completed");
    await nextPoll();
    expect(element("phase")).toHaveTextContent("Import complete — the server is ready");
    expect(element("return")).toHaveTextContent("Open the app");
    expect(element("return")).toBeVisible();
    expect(element("timeline").querySelectorAll('[data-state="done"]')).toHaveLength(7);
    await vi.advanceTimersByTimeAsync(10000);
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });
  it("tracks automatic shutdown without requesting another restart or showing completion early", async () => {
    let state = snapshot("queued", { automaticMaintenance: true });

    await mountPage(() => response(state));
    expect(element("phase")).toHaveTextContent("Preparing to begin");
    expect(element("explanation")).toHaveTextContent("no second confirmation or manual restart");
    expect(element("return")).not.toBeVisible();
    state = snapshot("stopping", {
      automaticMaintenance: true,
      lastActivityAtUtc: "2026-10-08T00:00:00Z",
    });
    await nextPoll();
    expect(element("phase")).toHaveTextContent("Saving and closing the app");
    expect(element("timeline").querySelector('[aria-current="step"]')).toHaveTextContent("Prepare");
    expect(element("explanation")).toHaveTextContent("Data changes begin only after it exits");
    expect(element("activity")).toHaveTextContent("Finishing background tasks may take time");
    expect(element("failure")).not.toBeVisible();
    expect(element("return")).not.toBeVisible();
    state = snapshot("starting", { automaticMaintenance: true });
    await nextPoll();
    expect(element("return")).not.toBeVisible();
    state = snapshot("completed", { automaticMaintenance: true });
    await nextPoll();
    expect(element("return")).toBeVisible();
  });
  it("reports a worker failure without offering monitor-token retry actions and observes later recovery", async () => {
    let state = snapshot("failed", {
      automaticMaintenance: true,
      failedPhase: "copying",
      error: "Maintenance worker exited unexpectedly",
    });

    const fetchMock = await mountPage(() => response(state));

    expect(element("failure")).toBeVisible();
    expect(element("error")).toHaveTextContent("Maintenance worker exited unexpectedly");
    expect(element("explanation")).toHaveTextContent("no automatic retry is running");
    expect(element("failureAdvice")).toHaveTextContent("fix the cause, then restart Bakabase");
    expect(document.querySelectorAll("button, form")).toHaveLength(0);
    expect(element("return")).not.toBeVisible();
    state = snapshot("scanning", {
      automaticMaintenance: true,
      startedAtUtc: new Date().toISOString(),
    });
    await nextPoll();
    expect(element("failure")).not.toBeVisible();
    expect(element("phase")).toHaveTextContent("Scanning source files");
    expect(fetchMock.mock.calls.every(([, options]) => !options.method)).toBe(true);
  });
  it("keeps the independent setup's last status through an outage without inventing success", async () => {
    let connected = true;

    await mountPage(() => {
      if (!connected) throw new TypeError("Setup connection failed");

      return response(snapshot("stopping", { automaticMaintenance: true }));
    });
    connected = false;
    await vi.advanceTimersByTimeAsync(33000);
    expect(element("phase")).toHaveTextContent("Saving and closing the app");
    expect(element("lastKnown")).toHaveTextContent("Saving and closing the app");
    expect(element("reconnectAdvice")).toHaveTextContent("Setup cannot be reached right now");
    expect(element("reconnectAdvice")).toHaveTextContent("If Bakabase has exited");
    expect(element("failure")).not.toBeVisible();
    expect(element("return")).not.toBeVisible();
  });
  it("retains the installation step when the worker verifies installed data again", async () => {
    let state = snapshot("installing");

    await mountPage(() => response(state));
    state = snapshot("verifying");
    await nextPoll();
    expect(element("phase")).toHaveTextContent("Checking the installed data");
    expect(element("timeline").querySelector('[aria-current="step"]')).toHaveTextContent(
      "Back up and install",
    );
    expect(element("explanation")).toHaveTextContent("installed databases");
    // A new attempt can restart copying; old reached stages must not survive it.
    state = snapshot("scanning", { startedAtUtc: new Date().toISOString() });
    await nextPoll();
    expect(element("timeline").querySelector('[aria-current="step"]')).toHaveTextContent("Scan");
    expect(element("timeline").children[4]).toHaveAttribute("data-state", "pending");
  });
  it("loads persisted failure details collapsed and escaped, then waits for recovery", async () => {
    const error = '<img src=x onerror="alert(1)"> IOException: volume unavailable';
    let state = snapshot("failed", { failedPhase: "verifying", error });

    await mountPage(() => response(state));
    expect(element("failure")).toBeVisible();
    expect(element("failureTitle")).toHaveTextContent("Failed during: Checking data integrity");
    expect(element("timeline").querySelector('[data-state="failed"]')).toHaveTextContent("Verify");
    expect(element<HTMLDetailsElement>("errorDetails").open).toBe(false);
    expect(element("error")).toHaveTextContent(error);
    expect(element("error").querySelector("img")).toBeNull();
    expect(element("failureAdvice")).toHaveTextContent("no automatic rollback");
    expect(element("file")).toHaveTextContent("covers/current.jpg");
    expect(element("bar")).not.toBeVisible();
    state = snapshot("scanning", { startedAtUtc: new Date().toISOString() });
    await nextPoll();
    expect(element("failure")).not.toBeVisible();
    expect(element("phase")).toHaveTextContent("Scanning source files");
    expect(element("connection")).toHaveAttribute("data-state", "online");
  });
  it.each([
    {
      operation: "initialize",
      title: "Create your library",
      advice: "write access",
      targetPath: null,
    },
    {
      operation: "relocate",
      title: "Move your data folder",
      advice: "previous data folder is retained",
      targetPath: "/new-data",
    },
    {
      operation: "import",
      title: "Import your data",
      advice: "backup and installation recover",
      targetPath: null,
    },
    {
      operation: "import",
      title: "Import data into a new location",
      advice: "import source and previous data folder are retained",
      targetPath: "/new-data",
    },
  ])("gives recovery guidance for $title", async ({ operation, title, advice, targetPath }) => {
    await mountPage(() =>
      response(
        snapshot("failed", {
          operation,
          targetPath,
          sourcePath: "/source-data",
          backupPath: "/previous-data",
          error: "Permission denied",
        }),
      ),
    );
    expect(element("title")).toHaveTextContent(title);
    expect(element("failureAdvice")).toHaveTextContent(advice);
    expect(element("failureAdvice")).toHaveTextContent("restart");
    if (targetPath) {
      expect(element("locations")).toHaveTextContent("/source-data");
      expect(element("locations")).toHaveTextContent("/new-data");
      expect(element("backup")).toHaveTextContent(
        "Retained previous data directory: /previous-data",
      );
    }
  });
  it("omits copy stages and source warnings when creating an empty library", async () => {
    await mountPage(() =>
      response(
        snapshot("starting", { operation: "initialize", backupPath: null, currentFile: null }),
      ),
    );
    expect(element("timeline").children).toHaveLength(2);
    expect(element("timeline")).toHaveTextContent("Create and start");
    expect(element("title")).toHaveTextContent("Create your library");
    expect(element("size").parentElement).not.toBeVisible();
    expect(element("speed").parentElement).not.toBeVisible();
    expect(element("elapsed")).toBeVisible();
    expect(element("backup")).not.toBeVisible();
    expect(element("footer")).not.toBeVisible();
    expect(element("return")).not.toBeVisible();
  });
  it("preserves the last stage through a long outage and reconnects without guessing an outcome", async () => {
    let connected = true;
    let state = snapshot("copying");
    const fetchMock = await mountPage(() => {
      if (!connected) throw new TypeError("Network connection failed");

      return response(state);
    });

    connected = false;
    await nextPoll();
    expect(element("connection")).toHaveTextContent("does not mean success or failure");
    expect(element("lastKnown")).toHaveTextContent("Last confirmed: Copying files");
    expect(element("phase")).toHaveTextContent("Copying files");
    expect(element("percent")).toHaveTextContent("50.0%");
    expect(element("bar")).not.toBeVisible();
    expect(element("failure")).not.toBeVisible();
    await vi.advanceTimersByTimeAsync(30000);
    expect(element("connection")).toHaveTextContent("Service status is still unconfirmed");
    expect(element("reconnectAdvice")).toHaveTextContent(
      "If the process has exited, restart it manually",
    );
    expect(element("reconnectAdvice")).toHaveTextContent("refreshing also reads the saved state");
    expect(fetchMock.mock.calls.length).toBeGreaterThan(20);
    connected = true;
    state = snapshot("starting");
    await nextPoll();
    expect(element("connection")).toHaveAttribute("data-state", "online");
    expect(element("lastKnown")).not.toBeVisible();
    expect(element("reconnectAdvice")).not.toBeVisible();
    expect(element("phase")).toHaveTextContent("starting the server");
    expect(element("return")).not.toBeVisible();
  });
  it("treats unavailable HTTP responses as unconfirmed even before any status has loaded", async () => {
    let status = 503;
    const fetchMock = await mountPage(() => response(snapshot("copying"), status));

    expect(element("lastKnown")).toHaveTextContent("No server status has been received");
    expect(element("failure")).not.toBeVisible();
    status = 200;
    await nextPoll();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(element("phase")).toHaveTextContent("Copying files");
  });
  it("aborts an unresponsive request and keeps retrying", async () => {
    const fetchMock = await mountPage(
      (options) =>
        new Promise((_resolve, reject) => {
          options.signal!.addEventListener("abort", () =>
            reject(new DOMException("Timed out", "AbortError")),
          );
        }),
    );

    await vi.advanceTimersByTimeAsync(5000);
    expect(element("connection")).toHaveTextContent("Reconnecting automatically");
    expect(element("lastKnown")).toHaveTextContent("No server status has been received");
    await nextPoll();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(element("failure")).not.toBeVisible();
  });
  it("handles unauthorized links separately without declaring the operation failed", async () => {
    let authorized = true;
    const fetchMock = await mountPage(() => response(snapshot("copying"), authorized ? 200 : 401));

    authorized = false;
    await nextPoll();
    expect(element("connection")).toHaveTextContent("invalid or unauthorized");
    expect(element("connection")).toHaveTextContent("does not mean the operation failed");
    expect(element("reconnectAdvice")).toHaveTextContent("latest server startup logs");
    expect(element("lastKnown")).toHaveTextContent("Copying files");
    expect(element("failure")).not.toBeVisible();
    await vi.advanceTimersByTimeAsync(60000);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
  it("reads saved state after refresh without inventing stage history", async () => {
    sessionStorage.setItem("bakabase.import.monitorToken", token);
    sessionStorage.setItem("bakabase.import.monitorLanguage", "en");
    history.replaceState({}, "", page);
    const fetchMock = await mountPage(() => response(snapshot("verifying")));

    expect(fetchMock.mock.calls[0][1].headers).toEqual({ "X-Bakabase-Import-Token": token });
    expect(element("phase")).toHaveTextContent("Checking data integrity");
    expect(element("timeline").children[4]).toHaveTextContent("not yet confirmed");
    expect(element("timeline").children[4]).not.toHaveTextContent("not started");
    expect(element("failure")).not.toBeVisible();
  });
  it("distinguishes a responsive service from an unchanged worker report", async () => {
    await mountPage(() =>
      response(snapshot("verifying", { lastActivityAtUtc: "2026-10-08T00:00:00Z" })),
    );
    expect(element("connection")).toHaveAttribute("data-state", "online");
    expect(element("activity")).toHaveTextContent("The server is responding");
    expect(element("activity")).toHaveTextContent("does not mean failure");
    expect(element("activity")).toHaveTextContent("integrity checks");
    expect(element("failure")).not.toBeVisible();
    expect(element("phase")).toHaveTextContent("Checking data integrity");
  });
  it("supports old snapshots without activity or failure-stage fields", async () => {
    await mountPage(() => response(snapshot("failed", { error: "Disk disconnected" })));
    expect(element("failureTitle")).toHaveTextContent("The operation did not finish");
    expect(element("timeline").querySelector('[data-state="failed"]')).toBeNull();
    expect(element("activity")).not.toBeVisible();
    expect(element("error")).toHaveTextContent("Disk disconnected");
  });
  it("asks for a valid link without making requests when the token is missing", async () => {
    history.replaceState({}, "", `${page}#lang=en`);
    const fetchMock = await mountPage(() => response(snapshot("copying")));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(element("phase")).toHaveTextContent("A valid progress link is required");
    expect(element("metrics")).not.toBeVisible();
    expect(element("timelineSection")).not.toBeVisible();
  });
});
