import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import LegacyTaskDetailModal from "../components/LegacyTaskDetailModal";
import { AcquisitionStatus } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  get: vi.fn(),
  portal: vi.fn(),
  history: vi.fn(),
  cancel: vi.fn(),
  retry: vi.fn(),
  resume: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      getAcquisition: api.get,
      cancelAcquisition: api.cancel,
      retryAcquisition: api.retry,
      resumeAcquisition: api.resume,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/Workflow/WorkflowRunDetailModal", () => ({ default: () => null }));
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => ({}) }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, value: string) => value,
}));
vi.mock("@/components/Acquire/SourceExecutionHistory", () => ({
  default: (props: any) => {
    api.history(props);
    return <div>retained history</div>;
  },
}));
vi.mock("@/components/Acquire/PlanTask/RuntimeOverviewSummary", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title, footer }: any) => <div role="dialog" aria-label={title}>{children}{footer}</div>,
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Progress: () => null,
  Spinner: () => null,
  Tooltip: ({ children }: any) => children,
  toast: { success: vi.fn() },
}));
beforeEach(() => vi.clearAllMocks());

it("shows a queued original download without reviving its old storage message or changing cancel eligibility", async () => {
  api.get.mockResolvedValue({ code: 0, data: {
    id: 4, resourceName: "Queued legacy download", status: AcquisitionStatus.Waiting,
    workflowRunId: 75, downloadActivity: "queued", error: "LOCAL_STORAGE_RESERVE_LOW", attentionTags: [],
  } });
  render(<LegacyTaskDetailModal taskId={4} recipes={[]} onChanged={vi.fn()} onEditRecipe={vi.fn()} />);
  expect(await screen.findByText("acquisition.downloadActivity.queued")).toBeVisible();
  expect(screen.queryByText("LOCAL_STORAGE_RESERVE_LOW")).toBeNull();
  expect(screen.queryByRole("button", { name: "acquisition.cancel" })).toBeNull();
  expect(screen.getByRole("button", { name: "acquisition.delete.single" })).toBeEnabled();
  expect(api.cancel).not.toHaveBeenCalled();
  expect(api.resume).not.toHaveBeenCalled();
});

it.each([AcquisitionStatus.Waiting, AcquisitionStatus.Failed, AcquisitionStatus.Completed])(
  "opens deleted legacy task status %s as history, without execution controls",
  async (status) => {
    api.get.mockResolvedValue({
      code: 0,
      data: {
        id: 4,
        resourceName: "Retained legacy task",
        status,
        isDeleted: true,
        workflowRunId: 75,
        targetDirectory: "/kept/final-files",
        recipeDefinitionId: 8,
      },
    });
    render(
      <LegacyTaskDetailModal taskId={4} recipes={[]} onChanged={vi.fn()} onEditRecipe={vi.fn()} />,
    );
    await screen.findByText("acquisition.delete.readOnly");
    expect(screen.getByText("/kept/final-files")).toBeVisible();
    for (const key of ["acquisition.cancel", "acquisition.retry", "acquisition.continue"])
      expect(screen.queryByRole("button", { name: key })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.execution" }));
    expect(api.portal).toHaveBeenCalledWith(expect.any(Function), { runId: 75, readOnly: true });
    fireEvent.click(screen.getByRole("button", { name: "acquire.history.title" }));
    expect(screen.getByText("retained history")).toBeVisible();
    expect(api.history).toHaveBeenCalledWith(expect.objectContaining({ readOnly: true }));
    expect(api.cancel).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
    expect(api.resume).not.toHaveBeenCalled();
  },
);
