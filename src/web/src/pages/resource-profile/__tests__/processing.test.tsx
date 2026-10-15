import type { ProcessingJob } from "../processingApi";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceProcessingTasks from "../components/ResourceProcessingTasks";
import { processingActions, processingApi, PROCESSING_JOBS_CHANGED } from "../processingApi";

const request = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({ default: { request } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, "aria-label": label }: any) => (
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
}));
const job = (overrides: Partial<ProcessingJob> = {}): ProcessingJob => ({
  id: "manager:job-1",
  jobId: "job-1",
  role: "Manager",
  ownerId: "server-b",
  ownerEpoch: "epoch-b",
  resourceId: 0,
  kind: "PlayableFiles",
  state: "Running",
  phase: "Processing",
  processedCount: 40,
  totalCount: null,
  attempt: 1,
  pauseRequested: false,
  cancelRequested: false,
  version: 7,
  createdAtUtc: "2026-10-15T01:00:00Z",
  updatedAtUtc: "2026-10-15T01:01:00Z",
  ...overrides,
});
let root: Root;
let host: HTMLDivElement;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  request.mockReset();
  request.mockResolvedValue([job()]);
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});
const render = async () =>
  act(async () =>
    root.render(
      <ResourceProcessingTasks
        sources={[
          {
            deviceId: "server-b",
            libraryEpoch: "epoch-b",
            name: "NAS server",
            kind: "managed",
            available: true,
          },
        ]}
      />,
    ),
  );
const button = (label: string) =>
  [...host.querySelectorAll<HTMLButtonElement>("button")].find(
    (item) => item.textContent === label,
  )!;

describe("processing task controls", () => {
  it("uses a job identity and expected version, and leaves unknown totals indeterminate", async () => {
    await render();
    expect(host.textContent).toContain("NAS server");
    expect(host.querySelector("progress")?.hasAttribute("value")).toBe(false);
    request.mockResolvedValueOnce(job({ state: "Paused", version: 8 }));
    await act(async () => button("resourceProfile.processing.action.pause").click());
    expect(request).toHaveBeenLastCalledWith(
      expect.objectContaining({
        path: "/resource-processing/jobs/job-1/control",
        body: { action: "pause", expectedVersion: 7 },
        method: "POST",
      }),
    );
    expect(button("resourceProfile.processing.action.resume")).toBeDefined();
    expect(host.querySelector("progress")).toBeNull();
  });
  it("refreshes a stale job version while retaining the control conflict for review", async () => {
    await render();
    request.mockRejectedValueOnce(new Error("Task changed elsewhere"));
    request.mockResolvedValueOnce([job({ state: "Paused", version: 9 })]);
    await act(async () => button("resourceProfile.processing.action.pause").click());
    const alert = host.querySelector('[role="alert"]');
    expect(alert?.querySelector("p")?.textContent).toBe("resourceProfile.processing.controlFailed");
    expect(alert?.querySelector("details")?.hasAttribute("open")).toBe(false);
    expect(alert?.querySelector("pre")?.textContent).toBe("Task changed elsewhere");
    request.mockResolvedValueOnce(job({ state: "Running", version: 10 }));
    await act(async () => button("resourceProfile.processing.action.resume").click());
    expect(request).toHaveBeenLastCalledWith(
      expect.objectContaining({ body: { action: "resume", expectedVersion: 9 } }),
    );
  });
  it("does not retry a conflicted result and explains a fresh run is needed", async () => {
    request.mockResolvedValue([job({ state: "Conflict", error: "RevisionMismatch" })]);
    await render();
    expect(button("resourceProfile.processing.action.retry")).toBeUndefined();
    expect(host.textContent).toContain("resourceProfile.processing.conflict");
    expect(host.querySelector("details pre")?.textContent).toContain("RevisionMismatch");
    expect(processingActions(job({ state: "Failed" }))).toEqual(["retry", "cancel"]);
    expect(processingActions(job({ state: "Conflict" }))).toEqual(["cancel"]);
    expect(processingActions(job({ cancelRequested: true }))).toEqual([]);
  });
  it("shows current resource progress separately from the completed resource count", async () => {
    request.mockResolvedValue([
      job({
        totalCount: 100,
        currentStepPhase: "DiscoveringFiles",
        currentStepProcessedCount: 2000,
        currentStepTotalCount: 3000,
      }),
    ]);
    await render();
    expect(host.textContent).toContain("40 / 100");
    expect(host.textContent).toContain("2,000 / 3,000");
    expect(host.textContent).toContain("resourceProfile.processing.phase.DiscoveringFiles");
    expect(host.querySelector("progress")?.value).toBe(40);
  });
  it("paginates bounded pages and returns to latest after a newly started task", async () => {
    request.mockResolvedValueOnce(
      Array.from({ length: 30 }, (_, i) =>
        job({ id: `role:${i}`, jobId: String(i), state: "Completed" }),
      ),
    );
    await render();
    request.mockResolvedValueOnce([job({ id: "older", state: "Completed" })]);
    await act(async () => button("resourceProfile.processing.older").click());
    expect(request).toHaveBeenLastCalledWith(
      expect.objectContaining({ query: { take: 30, beforeId: "role:29" } }),
    );
    expect(host.querySelectorAll("[data-job-id]")).toHaveLength(1);
    request.mockResolvedValueOnce([job({ state: "Completed" })]);
    await act(async () => {
      window.dispatchEvent(new Event(PROCESSING_JOBS_CHANGED));
    });
    expect(request).toHaveBeenLastCalledWith(expect.objectContaining({ query: { take: 30 } }));
  });
  it("starts only the selected saved rule and emits a refresh after acceptance", async () => {
    const changed = vi.fn();

    window.addEventListener(PROCESSING_JOBS_CHANGED, changed);
    request.mockResolvedValueOnce(job());
    await processingApi.start(17, 1);
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/resource-processing/jobs",
        body: { profileId: 17, kind: 1 },
        method: "POST",
      }),
    );
    expect(changed).toHaveBeenCalledTimes(1);
    window.removeEventListener(PROCESSING_JOBS_CHANGED, changed);
  });
  it("renders server timestamps without offsets as UTC in an Asia/Shanghai browser", async () => {
    const previous = process.env.TZ;

    process.env.TZ = "Asia/Shanghai";
    try {
      request.mockResolvedValue([job({ updatedAtUtc: "2026-10-15 01:01:00.000" })]);
      await render();
      expect(new Date(2026, 9, 15).getTimezoneOffset()).toBe(-480);
      expect(host.querySelector("time")?.dateTime).toBe("2026-10-15T01:01:00.000Z");
      expect(host.querySelector("time")?.textContent).toBe(
        new Date("2026-10-15T01:01:00Z").toLocaleString(),
      );
    } finally {
      if (previous === undefined) delete process.env.TZ;
      else process.env.TZ = previous;
    }
  });
});
