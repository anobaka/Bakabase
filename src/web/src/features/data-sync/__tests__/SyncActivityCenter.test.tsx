import userEvent from "@testing-library/user-event";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import SyncActivityCenter from "../components/SyncActivityCenter";
import { activityApi, type SyncActivity } from "../activityApi";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../activityApi", () => ({
  activityApi: {
    list: vi.fn(),
    metadataHistory: vi.fn(),
    processingHistory: vi.fn(),
    metadataPause: vi.fn(),
  },
}));
vi.mock("../api", () => ({ dataSyncApi: { syncNow: vi.fn() } }));
vi.mock("@/features/management/DeviceDot", () => ({
  default: ({ name }: { name: string }) => <i aria-label={name} />,
}));
vi.mock("@/pages/resource-profile/processingApi", () => ({
  processingActions: () => [],
  processingApi: { control: vi.fn() },
}));

const device = { id: "source", name: "NAS" };
const base: SyncActivity = {
  id: "metadata:source:resources",
  engine: "metadata",
  dataType: "metadata.resources",
  state: "Offline",
  phase: "Changes",
  source: device,
  destination: { id: "manager", name: "Windows" },
  cachedDataAvailable: true,
  pendingDecisions: 0,
  itemCount: 1000000,
  libraryEpoch: "epoch",
  errorCode: "MetadataSyncFailed",
  error: "Connection timed out",
};
const page = (items = [base]) => ({
  items,
  total: items.length,
  devices: [device],
  dataTypes: [base.dataType],
});
const onReview = vi.fn();
const renderCenter = () =>
  render(
    <SyncActivityCenter version={1} onDefinitionHistory={vi.fn()} onReviewDefinitions={onReview} />,
  );

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(activityApi.list).mockResolvedValue(page());
});

describe("unified sync activity", () => {
  it("shows the owning direction and offline cache without calling the network source", async () => {
    renderCenter();
    const row = await screen.findByTestId("sync-activity-row");

    expect(row.textContent).toContain("NAS");
    expect(row.textContent).toContain("Windows");
    expect(row.textContent).toContain("dataSync.activity.state.Offline");
    fireEvent.click(row.querySelector("button")!);
    expect(await screen.findByText("dataSync.activity.cached")).toBeTruthy();
    expect(screen.getByText("dataSync.activity.error.metadata.MetadataSyncFailed")).toBeTruthy();
    expect(screen.getByTestId("sync-error-technical")).not.toHaveAttribute("open");
    expect(screen.getByText(/Connection timed out/)).toBeTruthy();
    expect(screen.getByTestId("sync-activity-row").textContent).not.toContain(
      "dataSync.activity.state.Completed",
    );
  });

  it("sends data type, associated device and status filters to the same listing", async () => {
    renderCenter();
    await screen.findByTestId("sync-activity-row");
    await userEvent.click(screen.getByRole("button", { name: /dataSync.activity.filter.type/ }));
    await userEvent.click(
      await screen.findByRole("option", { name: "dataSync.activity.type.metadata.resources" }),
    );
    await userEvent.click(screen.getByRole("button", { name: /dataSync.activity.filter.device/ }));
    await userEvent.click(await screen.findByRole("option", { name: "NAS" }));
    await userEvent.click(screen.getByRole("button", { name: /dataSync.activity.filter.state/ }));
    await userEvent.click(
      await screen.findByRole("option", { name: "dataSync.activity.state.Offline" }),
    );
    await waitFor(() =>
      expect(activityApi.list).toHaveBeenLastCalledWith(
        { dataType: "metadata.resources", deviceId: "source", state: "Offline", offset: 0 },
        expect.any(AbortSignal),
      ),
    );
    await userEvent.click(screen.getByRole("button", { name: /dataSync.activity.filter.state/ }));
    await userEvent.click(
      await screen.findByRole("option", { name: "dataSync.activity.filter.allStates" }),
    );
    await waitFor(() =>
      expect(activityApi.list).toHaveBeenLastCalledWith(
        { dataType: "metadata.resources", deviceId: "source", state: undefined, offset: 0 },
        expect.any(AbortSignal),
      ),
    );
  });

  it("opens existing conflict controls with the exact type and peer", async () => {
    const row: SyncActivity = {
      ...base,
      id: "definition",
      engine: "definitions",
      dataType: "definitions.customProperty",
      state: "NeedsReview",
      linkId: 3,
      pendingDecisions: 2,
    };

    vi.mocked(activityApi.list).mockResolvedValue(page([row]));
    renderCenter();
    fireEvent.click((await screen.findByTestId("sync-activity-row")).querySelector("button")!);
    fireEvent.click(screen.getByText("dataSync.activity.review"));
    expect(onReview).toHaveBeenCalledWith("source", "customProperty");
  });

  it("loads bounded history only when asked and pages by the durable cursor", async () => {
    vi.mocked(activityApi.metadataHistory)
      .mockResolvedValueOnce({
        entries: [
          {
            id: 99,
            serverId: "source",
            name: "NAS",
            startedAtUtc: "2026-10-15T00:00:00Z",
            updatedAtUtc: "2026-10-15T00:01:00Z",
            state: "Interrupted",
            phase: "Snapshot",
            processedCount: 200,
            resourceCount: 200,
            errorCode: "ManagedSourceDenied",
            error: "RAW DENIAL",
          },
        ],
        nextBeforeId: 99,
      })
      .mockResolvedValue({ entries: [] });
    renderCenter();
    await screen.findByTestId("sync-activity-row");
    expect(activityApi.metadataHistory).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("dataSync.activity.metadataHistory"));
    expect(await screen.findByText("dataSync.activity.state.Interrupted")).toBeTruthy();
    expect(screen.getByText("dataSync.activity.error.metadata.ManagedSourceDenied")).toBeTruthy();
    expect(screen.getByTestId("sync-error-technical")).not.toHaveAttribute("open");
    fireEvent.click(screen.getByText("dataSync.activity.older"));
    await waitFor(() =>
      expect(activityApi.metadataHistory).toHaveBeenLastCalledWith(
        undefined,
        99,
        expect.any(AbortSignal),
      ),
    );
  });

  it("localizes request failures without displaying raw server messages as the explanation", async () => {
    vi.mocked(activityApi.list).mockRejectedValue(new Error("RAW SERVER FAILURE"));
    renderCenter();
    const alert = await screen.findByRole("alert");

    expect(alert.querySelector("p")).toHaveTextContent("dataSync.activity.loadFailed");
    expect(alert.querySelector("p")).not.toHaveTextContent("RAW SERVER FAILURE");
    expect(screen.getByTestId("sync-error-technical")).not.toHaveAttribute("open");
    expect(screen.getByTestId("sync-error-technical")).toHaveTextContent("RAW SERVER FAILURE");
  });

  it("separates processing owner and worker, and never turns a conflict into success", async () => {
    const row = {
      ...base,
      id: "work",
      engine: "processing",
      dataType: "processing.Enhancement",
      state: "Conflict",
      phase: "Applying",
      worker: { id: "worker", name: "Worker PC" },
      job: { role: "Manager", state: "Conflict" },
    } as SyncActivity;

    vi.mocked(activityApi.list).mockResolvedValue(page([row]));
    renderCenter();
    fireEvent.click((await screen.findByTestId("sync-activity-row")).querySelector("button")!);
    expect(screen.getByLabelText("Worker PC")).toBeTruthy();
    expect(screen.getByText("resourceProfile.processing.conflict")).toBeTruthy();
    expect(screen.getByTestId("sync-activity-row").textContent).not.toContain(
      "dataSync.activity.state.Completed",
    );
  });
});
