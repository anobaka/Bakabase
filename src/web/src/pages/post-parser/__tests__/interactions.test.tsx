import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { HeroUIProvider, Select as HeroSelect, SelectItem } from "@heroui/react";
import { StrictMode } from "react";
import userEvent from "@testing-library/user-event";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PostParserPage from "..";
import AddTasksModal from "../components/AddTasksModal";
import AddToAcquisitionModal from "../components/AddToAcquisitionModal";
import DownloadInfoResultRenderer from "../components/DownloadInfoResultRenderer";
import LocalProcessingModal from "../components/LocalProcessingModal";
import ContentReviewDialog from "../components/ContentReviewDialog";
import PostAcquisitionCatchUpModal from "../components/PostAcquisitionCatchUpModal";
import PostAcquisitionBatchCatchUpModal from "../components/PostAcquisitionBatchCatchUpModal";
import CloudDownloadModal from "@/components/CloudDownload/AsyncCloudDownloadModal";
import { missingParsingWorkflowRunError } from "../acquisitionEligibility";
import { getTaskDisplayStatus, taskStatusGroups } from "../taskStatus";
import { buildTaskSearchText } from "../search";

import {
  BTaskStatus,
  BTaskType,
  BTaskResourceType,
  PostParseTarget,
  PostParserSource,
  WorkflowRunStatus,
} from "@/sdk/constants";
import { usePostParserTasksStore } from "@/stores/postParserTasks";
import { useBTasksStore } from "@/stores/bTasks";
import { useSoulPlusOptionsStore } from "@/stores/options";

const api = vi.hoisted(() => ({
  add: vi.fn(),
  start: vi.fn(),
  retry: vi.fn(),
  reparse: vi.fn(),
  reparseFailed: vi.fn(),
  rows: vi.fn(),
  fullFetch: vi.fn(),
  search: vi.fn(),
  failedSelection: vi.fn(),
  previewDeletion: vi.fn(),
  previewPurchase: vi.fn(),
  confirmPurchase: vi.fn(),
  previewReview: vi.fn(),
  confirmReview: vi.fn(),
  readinessGet: vi.fn(),
  readinessConfirm: vi.fn(),
  readinessRevoke: vi.fn(),
  confirmDeletion: vi.fn(),
  remove: vi.fn(),
  removeAll: vi.fn(),
  import: vi.fn(),
  options: vi.fn(),
  request: vi.fn(),
  workflows: vi.fn(),
  runWorkflow: vi.fn(),
  manualWorkflow: vi.fn(),
  history: vi.fn(),
  acquisitionHistory: vi.fn(),
  navigate: vi.fn(),
  openUrl: vi.fn(),
  portal: vi.fn(),
  copy: vi.fn(),
  copied: vi.fn(),
  copyFailed: vi.fn(),
  renderDownloadInfo: vi.fn(),
  translate: (key: string) => key,
}));

// The shared test mock recreates t on every render; the real hook retains it
// until the language changes. Keep that contract for render-isolation checks.
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: api.translate, i18n: { language: "en", changeLanguage: vi.fn() } }),
  Trans: ({ children }: { children: ReactNode }) => children,
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));

vi.mock("../components/DownloadInfoResultRenderer", async (importOriginal) => {
  const original =
    await importOriginal<typeof import("../components/DownloadInfoResultRenderer")>();
  return {
    ...original,
    default: (props: Parameters<typeof original.default>[0]) => {
      api.renderDownloadInfo();
      return <original.default {...props} />;
    },
  };
});

vi.mock("@/sdk/BApi", () => ({
  default: {
    request: api.request,
    postParser: {
      addPostParserTasks: api.add,
      startAllPostParserTasks: api.start,
      retryPostParserTaskWorkflow: api.retry,
      reParsePostParserTask: api.reparse,
      reParseFailedPostParserTasks: api.reparseFailed,
      getAllPostParserTasks: api.fullFetch,
      searchPostParserTasks: api.search,
      getFailedPostParserTaskSelection: api.failedSelection,
      previewPostParserTaskDeletion: api.previewDeletion,
      confirmPostParserTaskDeletion: api.confirmDeletion,
      previewPostParserTaskPurchases: api.previewPurchase,
      confirmPostParserTaskPurchases: api.confirmPurchase,
      previewPostParserContentReview: api.previewReview,
      confirmPostParserContentReview: api.confirmReview,
      getPostParserContentReadiness: api.readinessGet,
      confirmPostParserContentReadiness: api.readinessConfirm,
      revokePostParserContentReadiness: api.readinessRevoke,
      deletePostParserTask: api.remove,
      deleteAllPostParserTasks: api.removeAll,
      importPostParserTaskToAcquisition: api.import,
      purchasePostParserTaskContent: api.request,
    },
    options: { patchThirdPartyOptions: api.options },
    workflow: { searchWorkflows: api.workflows, runWorkflowManually: api.manualWorkflow },
    downloadIntents: { startPostLocalProcessing: api.runWorkflow },
    workflowSources: { queryWorkflowSourceRuns: api.history },
    acquisition: { getAcquisitionPlansBySources: api.acquisitionHistory },
    gui: { openUrlInDefaultBrowser: api.openUrl },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/FileSystemSelector/components/Modal", () => ({ default: () => null }));
vi.mock("@/components/FileSystemSelector/components/Panel", () => ({ default: () => null }));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, kind: string) => kind,
}));
vi.mock("@/components/Workflow/Activities", () => ({
  getWorkflowActivityUI: () => ({ parseConfig: JSON.parse, Summary: () => null }),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({
  default: () => <span aria-hidden>site-icon</span>,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/components/Workflow/WorkflowBrowserModal", () => ({ default: () => null }));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/stores/options", async () => ({
  useThirdPartyOptionsStore: (select: (state: unknown) => unknown) =>
    select({ data: { automaticallyParsingPosts: false } }),
  useSoulPlusOptionsStore: (await import("zustand")).create<{
    data: {
      autoBuyThreshold: number;
      minimumRemainingCoins: number;
      maxConcurrency: number;
      requestInterval: number;
    };
  }>(() => ({
    data: { autoBuyThreshold: 0, minimumRemainingCoins: 0, maxConcurrency: 1, requestInterval: 0 },
  })),
}));
vi.mock("../components/ConfigurationModal", () => ({ default: () => null }));
// Keep workflow help's platform settings separate from the parsing and import interactions.
vi.mock("@/components/Workflow/WorkflowIntegrationHint", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyConfig/base/TampermonkeyInstallButton", () => ({
  default: () => null,
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <span>SoulPlus</span> }));
vi.mock("@/components/Workflow/WorkflowRunsDrawer", () => ({
  default: ({ runSourceKey, workflowName }: { runSourceKey: string; workflowName: string }) => (
    <div data-testid="runs">
      {runSourceKey} {workflowName}
    </div>
  ),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Select: ({ dataSource, ...props }: any) => (
    <HeroSelect {...props}>
      {(dataSource ?? []).map((item: any) => (
        <SelectItem key={item.value}>{item.label}</SelectItem>
      ))}
    </HeroSelect>
  ),
  toast: { success: api.copied, primary: api.copied, warning: api.copied, danger: api.copyFailed },
  Modal: ({
    children,
    title,
    footer,
    visible,
    onOk,
  }: {
    children: ReactNode;
    title?: ReactNode;
    footer?:
      | { actions?: string[]; okProps?: { children?: ReactNode; isDisabled?: boolean } }
      | ReactNode;
    visible?: boolean;
    onOk?: () => void;
  }) => {
    if (visible === false) return null;
    const simple = footer && typeof footer === "object" && "actions" in footer ? footer : undefined;

    return (
      <div role="dialog">
        <h2>{title}</h2>
        {children}
        {simple ? (
          <button disabled={simple.okProps?.isDisabled} onClick={onOk}>
            {simple.okProps?.children ?? "Confirm"}
          </button>
        ) : (
          (footer as ReactNode)
        )}
      </div>
    );
  },
}));

const base: PostParserTask = {
  id: 14,
  source: PostParserSource.SoulPlus,
  link: "https://example.com/post/14",
  title: "A post",
  targets: [PostParseTarget.DownloadInfo],
  revision: 3,
};
const liveRun = (runId: number, status: BTaskStatus, stage?: string) => ({
  id: `workflow.run.${runId}`,
  name: "Post parser",
  createdAt: "2026-10-05T00:00:00Z",
  isPersistent: false,
  type: BTaskType.Any,
  resourceType: BTaskResourceType.Any,
  status,
  data: { workflowRunId: runId, activityKind: "postParser.test", stage },
});
const data = {
  title: "A parsed resource",
  resources: [
    { link: "https://pan.example/one", code: "aBc1", password: "first password" },
    { link: "https://pan.example/two", code: "dEf2", password: "second password" },
  ],
};
const parsed = { ...base, results: { [PostParseTarget.DownloadInfo]: data } };
let container: HTMLDivElement;
let root: Root;
const show = (content: ReactNode) =>
  act(async () => {
    root.render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>);
  });
const byRole = (role: string, name?: string) =>
  Array.from(
    document.querySelectorAll<HTMLElement>(
      role === "button"
        ? "button"
        : role === "checkbox"
          ? 'input[type="checkbox"]'
          : `[role="${role}"]`,
    ),
  ).filter(
    (element) =>
      name == null || (element.getAttribute("aria-label") || element.textContent?.trim()) === name,
  );
const required = (element: HTMLElement | null | undefined, description: string) => {
  if (!element) throw new Error(`Missing ${description}`);

  return element;
};
const waitFor = async (assertion: () => void) => {
  let failure: unknown;

  for (let attempt = 0; attempt < 100; attempt++) {
    await act(async () => {
      await Promise.resolve();
    });
    try {
      assertion();

      return;
    } catch (error) {
      failure = error;
    }
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }
  throw failure;
};
const screen = {
  getByRole: (role: string, options?: { name: string }) =>
    required(byRole(role, options?.name)[0], role),
  getAllByRole: (role: string) => byRole(role),
  queryByRole: (role: string, options?: { name: string }) => byRole(role, options?.name)[0] ?? null,
  getByLabelText: (label: string) => {
    const field = Array.from(document.querySelectorAll<HTMLElement>("input, textarea")).find(
      (element) =>
        element.getAttribute("aria-label") === label ||
        Array.from(document.querySelectorAll("label")).some(
          (item) =>
            item.htmlFor === element.id && item.textContent?.replace(/\*/g, "").trim() === label,
        ),
    );

    return required(field, `label ${label}`);
  },
  getByText: (text: string | RegExp) =>
    required(
      Array.from(document.querySelectorAll<HTMLElement>("body *"))
        .reverse()
        .find((element) =>
          typeof text === "string"
            ? element.textContent === text
            : text.test(element.textContent ?? ""),
        ),
      String(text),
    ),
  getByTestId: (id: string) =>
    required(document.querySelector<HTMLElement>(`[data-testid="${id}"]`), id),
  findByRole: async (role: string, options?: { name: string }) => {
    await waitFor(() => {
      screen.getByRole(role, options);
    });

    return screen.getByRole(role, options);
  },
  findByText: async (text: string) => {
    await waitFor(() => {
      screen.getByText(text);
    });

    return screen.getByText(text);
  },
};

const scopedAction = async (name: string, filtered = false) => {
  await fireEvent.click(
    screen.getByRole("button", {
      name: filtered ? "postParser.actions.filtered" : "postParser.actions.all",
    }),
  );
  return screen.findByRole("menuitem", { name });
};
const filterByAttentionReason = (code: string) =>
  fireEvent.click(
    required(
      container.querySelector<HTMLElement>(`button[data-attention-reason-filter="${code}"]`),
      `${code} attention reason filter`,
    ),
  );

describe("post parser batch acquisition entry", () => {
  it("captures the current cross-page filter without creating tasks or using the deletion preview", async () => {
    usePostParserTasksStore.getState().setTasks([
      { ...parsed, id: 101, title: "Saved resource one" },
      { ...parsed, id: 102, title: "Saved resource two" },
    ]);
    await show(<PostParserPage />);
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "Saved resource" },
    });
    await fireEvent.click(await scopedAction("postParser.batchCatchUp.action", true));
    expect(api.portal.mock.lastCall![0]).toBe(PostAcquisitionBatchCatchUpModal);
    expect(api.portal.mock.lastCall![1].scope).toEqual({
      keyword: "Saved resource",
      status: "all",
      reasonCode: undefined,
    });
    expect(api.previewDeletion).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
    expect(api.reparse).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeDisabled();
    await act(async () => api.portal.mock.lastCall![1].onClosed());
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeEnabled();
  });
});
const reviewCategoryPurchases = async () => {
  const button = await screen.findByRole("button", {
    name: "postParser.contentPurchaseReview.title",
  });
  await waitFor(() => expect(button).toBeEnabled());
  await fireEvent.click(button);
};
const filterByStatus = (status: string) =>
  fireEvent.click(
    required(
      container.querySelector<HTMLElement>(`button[data-status-group="${status}"]`),
      `${status} status filter`,
    ),
  );
const fireEvent = {
  click: (element: HTMLElement) =>
    act(async () => {
      element.click();
    }),
  change: (element: HTMLElement, event: { target: { value: string } }) =>
    act(async () => {
      const prototype =
        element.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;

      Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, event.target.value);
      element.dispatchEvent(new Event("input", { bubbles: true }));
      // The production list debounces server-side keyword search.
      await new Promise((resolve) => window.setTimeout(resolve, 320));
    }),
};

const selectOption = async (label: string, key: string) => {
  await act(async () => userEvent.click(screen.getByRole("button", { name: label })));
  await act(async () =>
    userEvent.click(
      required(
        document.querySelector<HTMLElement>(`[role="option"][data-key="${key}"]`),
        `option ${key}`,
      ),
    ),
  );
};

describe("post parser server loading", () => {
  it("shows a centered initial loading state without reporting unknown task counts as empty", async () => {
    let finish!: (response: unknown) => void;
    api.search.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await show(<PostParserPage />);
    expect(container).toHaveTextContent("postParser.loading.tasks");
    expect(container).not.toHaveTextContent("postParser.page.empty");
    expect(container).not.toHaveTextContent("postParser.summary.total");
    expect(container.querySelector('[aria-busy="true"]')).toHaveClass(
      "items-center",
      "justify-center",
    );
    expect(
      screen.getByRole("button", { name: "postParser.action.reparseFailedUnknown" }),
    ).toBeDisabled();
    expect(screen.getByRole("button", { name: "postParser.actions.unknown" })).toBeDisabled();
    expect(api.search).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ pageIndex: 1, pageSize: 20 }),
      expect.anything(),
    );
    expect(api.fullFetch).not.toHaveBeenCalled();
    await act(async () =>
      finish({
        code: 0,
        data: {
          items: [],
          pageIndex: 1,
          pageSize: 20,
          totalCount: 0,
          globalTotal: 0,
          statusCounts: Object.fromEntries(taskStatusGroups.map((group) => [group, 0])),
          startableCount: 0,
          pendingHandoffCount: 0,
          runningStages: {},
        },
      }),
    );
    expect(container).toHaveTextContent("postParser.page.empty");
    expect(container).not.toHaveTextContent("postParser.loading.tasks");
  });

  it("allows a failed initial page to be retried without suggesting that there are no tasks", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    api.search.mockRejectedValueOnce(new Error("offline"));
    await show(<PostParserPage />);
    expect(container).toHaveTextContent("postParser.loading.failed");
    expect(container).not.toHaveTextContent("postParser.page.empty");
    await fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
    expect(container.querySelector('[data-task-id="14"]')).toBeVisible();
    expect(api.fullFetch).not.toHaveBeenCalled();
  });

  it("keeps the previous page visible but inert during navigation and preserves the scroller on ordinary refresh", async () => {
    const records = Array.from({ length: 41 }, (_, index) => ({ ...base, id: index + 1 }));
    usePostParserTasksStore.getState().setTasks(records);
    await show(<PostParserPage />);
    const oldScroller = required(
      container.querySelector<HTMLElement>("[data-post-parser-scroll]"),
      "initial scroller",
    );
    oldScroller.scrollTop = 420;
    let finish!: (response: unknown) => void;
    const search = api.search.getMockImplementation()!;
    api.search.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await fireEvent.click(
      required(container.querySelector<HTMLElement>('[aria-label="pagination item 2"]'), "page 2"),
    );
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(container).toHaveTextContent("postParser.loading.page");
    expect(container.querySelector('[role="table"]')!.parentElement!.parentElement!.inert).toBe(
      true,
    );
    expect(oldScroller.scrollTop).toBe(420);
    await act(async () => finish(await search({ status: "all", pageIndex: 2, pageSize: 20 })));
    const scroller = required(
      container.querySelector<HTMLElement>("[data-post-parser-scroll]"),
      "new page scroller",
    );
    expect(container.querySelector('[data-task-id="21"]')).toBeVisible();
    expect(scroller.scrollTop).toBe(0);
    scroller.scrollTop = 300;
    await act(async () =>
      usePostParserTasksStore
        .getState()
        .updateTask({ ...records[20], title: "Updated without navigation" }),
    );
    await waitFor(() =>
      expect(container.querySelector('[data-task-id="21"]')).toHaveTextContent(
        "Updated without navigation",
      ),
    );
    expect(container.querySelector("[data-post-parser-scroll]")).toBe(scroller);
    expect(scroller.scrollTop).toBe(300);
    expect(container).not.toHaveTextContent("postParser.loading.page");
    expect(api.fullFetch).not.toHaveBeenCalled();
  });
});

describe("previewing and confirming scoped task deletion", () => {
  it("previews across matching pages and confirms exactly that snapshot rather than later matches", async () => {
    const records = Array.from({ length: 65 }, (_, index) => ({
      ...base,
      id: index + 1,
      title: index < 60 ? "Selected post" : "Another post",
      workflowRunId: index + 1000,
      workflowStatus: index < 30 ? WorkflowRunStatus.Success : WorkflowRunStatus.Failed,
    }));
    usePostParserTasksStore.getState().setTasks(records);
    await show(<PostParserPage />);
    await filterByStatus("failed");
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "  Selected  " },
    });
    const button = await scopedAction("postParser.action.deleteFiltered", true);
    await fireEvent.click(button);
    expect(api.previewDeletion).toHaveBeenCalledExactlyOnceWith(
      { keyword: "Selected", status: "failed", reasonCode: undefined },
      expect.objectContaining({ sensitiveResponse: true }),
    );
    expect(api.confirmDeletion).not.toHaveBeenCalled();
    expect(api.removeAll).not.toHaveBeenCalled();
    const preview = api.portal.mock.lastCall![1];
    expect(preview).toMatchObject({ count: 30, keyword: "Selected", status: "failed" });
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeDisabled();
    await act(async () =>
      usePostParserTasksStore.getState().updateTask({ ...records[30], id: 80 }),
    );
    api.confirmDeletion.mockResolvedValueOnce({
      code: 0,
      data: { requestedCount: 30, deletedCount: 29, skippedCount: 1, items: [] },
    });
    await act(async () => preview.onConfirm());
    expect(api.confirmDeletion).toHaveBeenCalledExactlyOnceWith({
      scope: { keyword: "Selected", status: "failed" },
      tasks: records.slice(30, 60).map((task) => ({
        taskId: task.id,
        revision: task.revision,
        workflowRunId: task.workflowRunId,
      })),
    });
    expect(api.copied).toHaveBeenCalledWith("postParser.deleteBatch.result");
    expect(api.removeAll).not.toHaveBeenCalled();
    expect(api.remove).not.toHaveBeenCalled();
  });

  it("uses a confirmation even without filters and cancellation never sends a deletion request", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await fireEvent.click(await scopedAction("postParser.action.deleteAllCount"));
    const preview = api.portal.mock.lastCall![1];
    expect(preview).toMatchObject({ count: 1, keyword: "", status: "all" });
    expect(api.previewDeletion).toHaveBeenCalledExactlyOnceWith(
      { keyword: undefined, status: "all", reasonCode: undefined },
      expect.anything(),
    );
    await act(async () => preview.onClose());
    expect(api.confirmDeletion).not.toHaveBeenCalled();
    expect(api.removeAll).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "postParser.actions.all" })).toBeEnabled();
  });

  it("prevents repeat previews while the selection is loading or confirmation is open", async () => {
    let finish!: (response: unknown) => void;
    const original = api.previewDeletion.getMockImplementation()!;
    api.previewDeletion.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    const button = await scopedAction("postParser.action.deleteAllCount");
    act(() => {
      button.click();
      button.click();
    });
    expect(api.previewDeletion).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "postParser.actions.all" })).toBeDisabled();
    expect(api.portal).not.toHaveBeenCalled();
    await act(async () => finish(await original({ status: "all" })));
    expect(api.portal).toHaveBeenCalledTimes(1);
    await fireEvent.click(button);
    expect(api.previewDeletion).toHaveBeenCalledTimes(1);
    expect(api.confirmDeletion).not.toHaveBeenCalled();
  });

  it("discards a preview returned after the user changes its filter", async () => {
    let finish!: (response: unknown) => void;
    api.previewDeletion.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await fireEvent.click(await scopedAction("postParser.action.deleteAllCount"));
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "Another query" },
    });
    await act(async () =>
      finish({
        code: 0,
        data: {
          scope: { status: "all", keyword: "" },
          count: 1,
          tasks: [{ taskId: base.id, revision: base.revision }],
        },
      }),
    );
    expect(api.portal).not.toHaveBeenCalled();
    expect(container).toHaveTextContent("postParser.deleteBatch.queryChanged");
    expect(api.confirmDeletion).not.toHaveBeenCalled();
    expect(api.removeAll).not.toHaveBeenCalled();
  });

  it("discards a delayed preview after leaving the parser page", async () => {
    let finish!: (response: unknown) => void;
    api.previewDeletion.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await fireEvent.click(await scopedAction("postParser.action.deleteAllCount"));
    await show(<div>Another page</div>);
    await act(async () =>
      finish({
        code: 0,
        data: {
          scope: { status: "all", keyword: "" },
          count: 1,
          tasks: [{ taskId: base.id, revision: base.revision }],
        },
      }),
    );
    expect(api.portal).not.toHaveBeenCalled();
    expect(api.confirmDeletion).not.toHaveBeenCalled();
    expect(api.removeAll).not.toHaveBeenCalled();
  });

  it("keeps deletion disabled during pending search, empty results and failed refreshes", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    const input = screen.getByLabelText("postParser.search.label");
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
        input,
        "missing",
      );
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeDisabled();
    await waitFor(() => expect(container).toHaveTextContent("postParser.search.empty"));
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.search.clear" }));
    api.search.mockRejectedValueOnce(new Error("offline"));
    await act(async () => {
      window.dispatchEvent(new Event("focus"));
    });
    expect(container).toHaveTextContent("postParser.loading.refreshFailed");
    const button = screen.getByRole("button", { name: "postParser.actions.all" });
    expect(button).toBeDisabled();
    await fireEvent.click(button);
    expect(api.previewDeletion).not.toHaveBeenCalled();
    expect(api.confirmDeletion).not.toHaveBeenCalled();
  });

  it.each([
    { scope: { status: "all", keyword: "" }, count: 2, tasks: [{ taskId: 14, revision: 3 }] },
    { scope: { status: "failed", keyword: "" }, count: 1, tasks: [{ taskId: 14, revision: 3 }] },
  ])(
    "rejects an inconsistent preview without displaying an actionable confirmation: %j",
    async (data) => {
      usePostParserTasksStore.getState().setTasks([base]);
      api.previewDeletion.mockResolvedValueOnce({ code: 0, data });
      await show(<PostParserPage />);
      await fireEvent.click(await scopedAction("postParser.action.deleteAllCount"));
      expect(api.portal).not.toHaveBeenCalled();
      expect(api.confirmDeletion).not.toHaveBeenCalled();
      expect(api.removeAll).not.toHaveBeenCalled();
    },
  );

  it("does not open confirmation when no tasks remain in the fresh selection", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    api.previewDeletion.mockResolvedValueOnce({
      code: 0,
      data: { scope: { status: "all", keyword: "" }, count: 0, tasks: [] },
    });
    await show(<PostParserPage />);
    await fireEvent.click(await scopedAction("postParser.action.deleteAllCount"));
    expect(api.copied).toHaveBeenCalledWith("postParser.deleteBatch.none");
    expect(api.portal).not.toHaveBeenCalled();
    expect(api.confirmDeletion).not.toHaveBeenCalled();
  });
});

describe("projected attention filters and current-scope purchase actions", () => {
  const attention = (id: number, codes: string[]): PostParserTask => ({
    ...base,
    id,
    workflowRunId: id + 100,
    workflowStatus: WorkflowRunStatus.Waiting,
    parsingState: "awaitingPurchase",
    attentionReasonCodes: codes,
  });
  it("shows every projected reason on its row and falls back for unknown codes without inferring error text", async () => {
    const originalTranslate = api.translate;
    api.translate = (key) =>
      key === "postParser.attentionReason.overPurchaseLimit" ? "Over purchase limit" : key;
    try {
      usePostParserTasksStore
        .getState()
        .setTasks([
          attention(14, ["overPurchaseLimit", "priceUnknown", "futureReason"]),
          { ...base, id: 15, error: "priceUnknown overPurchaseLimit" },
        ]);
      await show(<PostParserPage />);
      const row = required(
        container.querySelector<HTMLElement>('[data-task-id="14"]'),
        "attention row",
      );
      expect(
        [...row.querySelectorAll("[data-attention-reason]")].map((chip) =>
          chip.getAttribute("data-attention-reason"),
        ),
      ).toEqual(["overPurchaseLimit", "priceUnknown", "futureReason"]);
      expect(row.querySelector('[data-attention-reason="overPurchaseLimit"]')).toHaveTextContent(
        "Over purchase limit",
      );
      expect(row.querySelector('[data-attention-reason="futureReason"]')).toHaveTextContent(
        "postParser.attentionReason.other",
      );
      expect(container.querySelector('[data-task-id="15"] [data-attention-reason]')).toBeNull();
    } finally {
      api.translate = originalTranslate;
    }
  });

  it("opens scoped actions without loading quotes or exporting task payloads", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    const initialSearches = api.search.mock.calls.length;
    await fireEvent.click(screen.getByRole("button", { name: "postParser.actions.all" }));
    expect(screen.queryByRole("menuitem", { name: "postParser.purchaseBatch.action" })).toBeNull();
    expect(
      screen.getByRole("menuitem", { name: "postParser.actions.exportInstructions" }),
    ).toBeVisible();
    expect(api.previewPurchase).not.toHaveBeenCalled();
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.previewDeletion).not.toHaveBeenCalled();
    expect(api.search).toHaveBeenCalledTimes(initialSearches);
    expect(api.fullFetch).not.toHaveBeenCalled();
  });

  it("filters by projected reasons, keeps overlapping counts and resets reason when leaving attention", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([
        attention(1, ["priceUnknown", "purchaseRequired"]),
        attention(2, ["purchaseRequired"]),
        attention(3, ["balanceUnknown"]),
      ]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    await filterByAttentionReason("priceUnknown");
    expect(api.search.mock.lastCall?.[0]).toMatchObject({
      status: "attention",
      reasonCode: "priceUnknown",
      pageIndex: 1,
    });
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(container.querySelector('[data-task-id="2"]')).toBeNull();
    expect(container).not.toHaveTextContent("postParser.filters.reasonHint");
    const selectedReason = required(
      container.querySelector<HTMLElement>('button[data-attention-reason-filter="priceUnknown"]'),
      "selected reason",
    );
    expect(selectedReason).toHaveAttribute("aria-pressed", "true");
    const otherReason = required(
      container.querySelector<HTMLElement>(
        'button[data-attention-reason-filter="purchaseRequired"]',
      ),
      "other overlapping reason",
    );
    expect(otherReason).toBeVisible();
    expect(otherReason).toHaveTextContent("2");
    await filterByAttentionReason("all");
    await filterByStatus("failed");
    expect(api.search.mock.lastCall?.[0]).not.toHaveProperty("reasonCode");
    expect(container.querySelector('[data-task-id="1"]')).toBeNull();
  });

  it("carries attention reason into deletion preview and its fixed confirmation scope", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([attention(1, ["priceUnknown"]), attention(2, ["balanceUnknown"])]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    await filterByAttentionReason("priceUnknown");
    await fireEvent.click(await scopedAction("postParser.action.deleteFiltered", true));
    expect(api.previewDeletion.mock.lastCall?.[0]).toMatchObject({
      status: "attention",
      reasonCode: "priceUnknown",
    });
    const preview = api.portal.mock.lastCall![1];
    expect(preview).toMatchObject({ count: 1, reasonCode: "priceUnknown" });
    await act(async () => preview.onConfirm());
    expect(api.confirmDeletion.mock.lastCall?.[0]).toMatchObject({
      scope: { status: "attention", reasonCode: "priceUnknown" },
      tasks: [{ taskId: 1 }],
    });
  });

  it("keeps status, reason and review summaries in the single main viewport while search and pagination remain outside", async () => {
    usePostParserTasksStore.getState().setTasks([attention(14, ["purchaseRequired"])]);
    await show(<PostParserPage />);
    const viewport = required(
      container.querySelector<HTMLElement>("[data-post-parser-scroll]"),
      "main viewport",
    );
    expect(container.querySelectorAll("[data-post-parser-scroll]")).toHaveLength(1);
    expect(viewport).toContainElement(
      screen.getByRole("group", { name: "postParser.summary.label" }),
    );
    expect(viewport).not.toContainElement(screen.getByLabelText("postParser.search.label"));
    expect(viewport).not.toContainElement(
      required(
        container.querySelector<HTMLElement>('[aria-label="postParser.pagination.label"]'),
        "pagination",
      ),
    );
    await filterByStatus("attention");
    await waitFor(() =>
      expect(
        container.querySelector('[data-attention-reason-filter="purchaseRequired"]'),
      ).toBeVisible(),
    );
    expect(container.querySelector("[data-post-parser-scroll]")).toBe(viewport);
    expect(viewport).toContainElement(
      screen.getByRole("group", { name: "postParser.filters.reason" }),
    );
    expect(viewport).toContainElement(screen.getByText("postParser.contentPurchaseReview.summary"));
    expect(container.querySelector('[role="rowgroup"]')).not.toHaveClass("overflow-y-auto");
    viewport.scrollTop = 420;
    await act(async () =>
      usePostParserTasksStore.getState().updateTask(attention(15, ["priceUnknown"])),
    );
    await waitFor(() =>
      expect(
        container.querySelector('[data-attention-reason-filter="priceUnknown"]'),
      ).toBeVisible(),
    );
    expect(container.querySelectorAll("[data-post-parser-scroll]")).toHaveLength(1);
    expect(container.querySelector("[data-post-parser-scroll]")).toBe(viewport);
    expect(viewport.scrollTop).toBe(420);
    expect(api.previewPurchase).not.toHaveBeenCalled();
    expect(api.previewReview).not.toHaveBeenCalled();
  });

  it("opens the unified review with the fixed current reason scope without loading or purchasing from the page", async () => {
    usePostParserTasksStore.getState().setTasks([attention(14, ["purchaseRequired"])]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    await filterByAttentionReason("purchaseRequired");
    await reviewCategoryPurchases();
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.previewPurchase).not.toHaveBeenCalled();
    expect(api.previewReview).not.toHaveBeenCalled();
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
    const preview = api.portal.mock.lastCall![1];
    expect(preview.scope).toEqual({
      keyword: undefined,
      status: "attention",
      reasonCode: "purchaseRequired",
    });
    expect(preview).not.toHaveProperty("onConfirm");
    await act(async () =>
      usePostParserTasksStore.getState().updateTask(attention(80, ["purchaseRequired"])),
    );
    expect(preview.scope).toEqual({
      keyword: undefined,
      status: "attention",
      reasonCode: "purchaseRequired",
    });
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.confirmReview).not.toHaveBeenCalled();
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
  });

  it("canceling the unified review releases page actions without confirming or starting parsing", async () => {
    usePostParserTasksStore.getState().setTasks([attention(14, ["purchaseRequired"])]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    expect(api.previewPurchase).not.toHaveBeenCalled();
    expect(api.previewReview).not.toHaveBeenCalled();
    await filterByAttentionReason("purchaseRequired");
    await reviewCategoryPurchases();
    await act(async () => api.portal.mock.lastCall![1].onClose());
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.confirmReview).not.toHaveBeenCalled();
    expect(api.reparse).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "postParser.actions.filtered" })).toBeEnabled();
  });

  it("routes over-limit tasks to review without treating their legacy quote as purchase approval", async () => {
    usePostParserTasksStore.getState().setTasks([attention(14, ["overPurchaseLimit"])]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    await filterByAttentionReason("overPurchaseLimit");
    await reviewCategoryPurchases();
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.portal.mock.lastCall![1].scope).toMatchObject({
      status: "attention",
      reasonCode: "overPurchaseLimit",
    });
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it.each(["query", "unmount"])(
    "never turns a stale review callback into a purchase after %s changes",
    async (mode) => {
      usePostParserTasksStore.getState().setTasks([attention(14, ["purchaseRequired"])]);
      await show(<PostParserPage />);
      await filterByStatus("attention");
      await filterByAttentionReason("purchaseRequired");
      await reviewCategoryPurchases();
      const review = api.portal.mock.lastCall![1];
      if (mode === "unmount") await show(<div>Another page</div>);
      else
        await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
          target: { value: "new query" },
        });
      const searches = api.search.mock.calls.length;
      await act(async () => review.onChanged());
      if (mode === "unmount") expect(api.search).toHaveBeenCalledTimes(searches);
      expect(review.scope).toEqual({
        keyword: undefined,
        status: "attention",
        reasonCode: "purchaseRequired",
      });
      expect(api.confirmPurchase).not.toHaveBeenCalled();
      expect(api.confirmReview).not.toHaveBeenCalled();
      expect(api.request).not.toHaveBeenCalled();
    },
  );

  it("does not broaden a category review when another overlapping reason is selected later", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([attention(14, ["priceUnknown", "purchaseRequired"])]);
    await show(<PostParserPage />);
    await filterByStatus("attention");
    await filterByAttentionReason("priceUnknown");
    await reviewCategoryPurchases();
    const first = api.portal.mock.lastCall![1];
    await act(async () => first.onClose());
    await filterByAttentionReason("purchaseRequired");
    await reviewCategoryPurchases();
    expect(first.scope.reasonCode).toBe("priceUnknown");
    expect(api.portal.mock.lastCall![1].scope.reasonCode).toBe("purchaseRequired");
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.confirmReview).not.toHaveBeenCalled();
  });

  it("keeps a partially unlocked task awaiting its remaining items instead of calling it complete", async () => {
    const task: PostParserTask = {
      ...attention(14, ["overPurchaseLimit"]),
      contentSnapshot: {
        capturedAt: "2026-10-23T00:00:00Z",
        mainHtml: "saved post",
        commentHtmlList: [],
        locks: [
          { url: "https://example.com/lock/1", isBought: true, price: 3 },
          { url: "https://example.com/lock/2", isBought: false, price: 20 },
        ],
      },
    };
    usePostParserTasksStore.getState().setTasks([task]);
    await show(<PostParserPage />);
    expect(container.querySelector('[data-task-id="14"]')).toHaveTextContent(
      "postParser.purchaseScope.partiallyUnlocked",
    );
    expect(container.querySelector('[data-task-id="14"]')).toHaveTextContent(
      "postParser.state.awaitingPurchase",
    );
    expect(container.querySelector('[data-task-id="14"]')).not.toHaveTextContent(
      "postParser.state.complete",
    );
  });

  it("allows completed acquisition information despite optional unpaid blocks and opens unified review on demand", async () => {
    const record: PostParserTask = {
      ...base,
      workflowStatus: WorkflowRunStatus.Success,
      parsingState: "complete",
      contentSnapshot: {
        currencyCode: "SP",
        currencyName: "SP币",
        mainHtml: "Download information is in the body",
        locks: [
          {
            blockId: "proof",
            isBought: false,
            url: "https://example.com/proof",
            priceText: "9007199254740993.123",
            contextText: "Purchase proof",
          },
        ],
        readiness: {
          basis: "manual",
          fingerprint: "current",
          policyVersion: "v1",
          decisions: [
            {
              blockId: "proof",
              relevance: "optional",
              purpose: "purchaseProof",
              evidence: ["Purchase proof"],
            },
          ],
        },
      },
      results: {
        DownloadInfo: { isComplete: true, resources: [{ link: "https://example.com/file" }] },
      },
    };
    usePostParserTasksStore.getState().setTasks([record]);
    await show(<PostParserPage />);
    const row = required(
      container.querySelector<HTMLElement>('[data-task-id="14"]'),
      "completed optional-content row",
    );
    expect(row).toHaveTextContent("postParser.state.complete");
    expect(row).toHaveTextContent("9007199254740993.123 SP币");
    expect(row).toHaveTextContent("postParser.contentReadiness.manualBadge");
    expect(screen.getByRole("button", { name: "acquire.plan.create" })).toBeEnabled();
    expect(api.readinessGet).not.toHaveBeenCalled();
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
    );
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.portal.mock.lastCall![1].scope).toEqual({ status: "all", taskIds: [record.id] });
    expect(api.readinessConfirm).not.toHaveBeenCalled();
    expect(record.contentSnapshot!.locks[0].isBought).toBe(false);
  });

  it("shows the full view-content action and translates evidence-insufficient codes without changing records", async () => {
    const task: PostParserTask = {
      ...base,
      content: "saved post",
      parsingMessage: "POST_AVAILABILITY_EVIDENCE_INSUFFICIENT",
      availability: {
        status: "unknown",
        reason: "POST_AVAILABILITY_EVIDENCE_INSUFFICIENT",
        evidence: [],
      },
    };
    usePostParserTasksStore.getState().setTasks([task]);
    await show(<PostParserPage />);
    const button = screen.getByRole("button", { name: "postParser.action.viewContent" });
    expect(button).toHaveTextContent("postParser.action.viewContent");
    await fireEvent.click(button);
    const detail = api.portal.mock.lastCall![1].children;
    await show(detail);
    expect(container).toHaveTextContent("postParser.message.availabilityEvidenceInsufficient");
    expect(container).not.toHaveTextContent("POST_AVAILABILITY_EVIDENCE_INSUFFICIENT");
    expect(task.parsingMessage).toBe("POST_AVAILABILITY_EVIDENCE_INSUFFICIENT");
  });
});

describe("post parser rendering isolation", () => {
  const parsedPage = () =>
    Array.from({ length: 20 }, (_, index) => ({
      ...parsed,
      id: index + 1,
      title: `Record ${index + 1}`,
    }));

  it("does not render the twenty parsed result panels again for each search keystroke", async () => {
    usePostParserTasksStore.getState().setTasks(parsedPage());
    await show(<PostParserPage />);
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(20);
    const rendered = api.renderDownloadInfo.mock.calls.length;
    expect(rendered).toBeGreaterThanOrEqual(20);
    const input = screen.getByLabelText("postParser.search.label");
    for (const value of ["R", "Re", "Rec", "Reco", "Record"]) {
      await act(async () => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
          input,
          value,
        );
        input.dispatchEvent(new Event("input", { bubbles: true }));
      });
      expect(input).toHaveValue(value);
      expect(api.renderDownloadInfo).toHaveBeenCalledTimes(rendered);
    }
    expect(api.search).toHaveBeenCalledTimes(1);
    expect(container).toHaveTextContent("postParser.loading.page");
    expect(container.querySelector('[role="table"]')!.parentElement!.parentElement!.inert).toBe(
      true,
    );
  });

  it("ignores unrelated progress and unchanged parser stage while still updating a real stage transition", async () => {
    const records = parsedPage();
    records[0] = { ...records[0], workflowRunId: 37, workflowStatus: WorkflowRunStatus.Running };
    usePostParserTasksStore.getState().setTasks(records);
    const visible = liveRun(37, BTaskStatus.Running, "extracting");
    const unrelated = liveRun(900, BTaskStatus.Running, "downloading");
    useBTasksStore.getState().setTasks([visible, unrelated]);
    await show(<PostParserPage />);
    const rendered = api.renderDownloadInfo.mock.calls.length;
    for (let percentage = 1; percentage <= 15; percentage++)
      await act(async () => {
        useBTasksStore.getState().setTasks([
          { ...visible, percentage },
          { ...unrelated, percentage },
        ]);
      });
    expect(api.renderDownloadInfo).toHaveBeenCalledTimes(rendered);
    expect(container.querySelector('[data-task-id="1"]')).toHaveTextContent(
      "postParser.stage.extracting",
    );
    await act(async () =>
      useBTasksStore
        .getState()
        .setTasks([liveRun(37, BTaskStatus.Running, "checkingLinks"), unrelated]),
    );
    expect(container.querySelector('[data-task-id="1"]')).toHaveTextContent(
      "postParser.stage.checkingLinks",
    );
    expect(api.renderDownloadInfo.mock.calls.length).toBeGreaterThan(rendered);
  });
});

describe("post parser manual pagination and filters", () => {
  const records = (count: number) =>
    Array.from({ length: count }, (_, index) => ({
      ...base,
      id: index + 1,
      title: `Record ${index + 1}`,
    }));
  const pageButton = (page: number, active = false) =>
    required(
      container.querySelector<HTMLElement>(
        `[aria-label="pagination item ${page}${active ? " active" : ""}"]`,
      ),
      `page ${page}`,
    );
  const goToPage = (page: number) => fireEvent.click(pageButton(page));

  it("pages all search matches and loads associated history only for the current page", async () => {
    usePostParserTasksStore.getState().setTasks(records(55));
    await show(<PostParserPage />);
    await waitFor(() => expect(api.history).toHaveBeenCalled());
    expect(api.history.mock.calls.at(-1)![0].sources.map((source: any) => source.entityId)).toEqual(
      Array.from({ length: 20 }, (_, i) => String(i + 1)),
    );
    expect(container.querySelector('[data-task-id="21"]')).toBeNull();
    await goToPage(2);
    await waitFor(() => expect(container.querySelector('[data-task-id="21"]')).toBeVisible());
    expect(container.querySelector('[data-task-id="1"]')).toBeNull();
    expect(
      api.acquisitionHistory.mock.calls.at(-1)![0].sources.map((source: any) => source.entityId),
    ).toEqual(Array.from({ length: 20 }, (_, i) => String(i + 21)));
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "Record 55" },
    });
    expect(container.querySelector('[data-task-id="55"]')).toBeVisible();
    expect(pageButton(1, true)).toBeInTheDocument();
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "2");
  });

  it("keeps the chosen page on live updates and clamps a removed last page without bouncing back", async () => {
    const initial = records(41);
    usePostParserTasksStore.getState().setTasks(initial);
    await show(<PostParserPage />);
    await goToPage(3);
    expect(container.querySelector('[data-task-id="41"]')).toBeVisible();
    await act(async () =>
      usePostParserTasksStore.getState().updateTask({ ...initial[40], title: "Updated record" }),
    );
    await waitFor(() =>
      expect(container.querySelector('[data-task-id="41"]')).toHaveTextContent("Updated record"),
    );
    expect(pageButton(3, true)).toBeInTheDocument();
    await act(async () => usePostParserTasksStore.getState().deleteTask(41));
    await waitFor(() => expect(pageButton(2, true)).toBeInTheDocument());
    await act(async () => usePostParserTasksStore.getState().updateTask(initial[40]));
    expect(pageButton(2, true)).toBeInTheDocument();
    expect(container.querySelector('[data-task-id="41"]')).toBeNull();
  });

  it("resets to the first page when changing the page size", async () => {
    usePostParserTasksStore.getState().setTasks(
      records(61).map((task) => ({
        ...task,
        workflowRunId: task.id + 1000,
        workflowStatus: WorkflowRunStatus.Pending,
      })),
    );
    useBTasksStore.getState().setTasks([liveRun(1045, BTaskStatus.Running, "extracting")]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.locateRunning" }));
    expect(container.querySelector('[data-task-id="45"]')).toHaveAttribute("data-located", "true");
    await selectOption("postParser.pagination.pageSize", "50");
    expect(pageButton(1, true)).toBeInTheDocument();
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(container.querySelector<HTMLElement>("[data-post-parser-scroll]")!.scrollTop).toBe(0);
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "51");
    expect(api.acquisitionHistory.mock.calls.at(-1)![0].sources).toHaveLength(50);
    await goToPage(2);
    expect(container.querySelector('[data-task-id="51"]')).toBeVisible();
  });

  it("combines status with keyword search, retains global counts and clears both filters", async () => {
    usePostParserTasksStore.getState().setTasks(
      records(45).map((task, index) => ({
        ...task,
        workflowStatus: index >= 20 ? WorkflowRunStatus.Failed : WorkflowRunStatus.Success,
        title: index === 44 ? "Unique failure" : task.title,
      })),
    );
    await show(<PostParserPage />);
    await goToPage(2);
    await filterByStatus("failed");
    expect(pageButton(1, true)).toBeInTheDocument();
    expect(container.querySelector('[data-task-id="21"]')).toBeVisible();
    expect(container.querySelector('[data-task-id="1"]')).toBeNull();
    const overview = screen.getByRole("group", { name: "postParser.summary.label" });
    expect(overview.querySelector('[data-status-group="success"]')).toHaveTextContent("20");
    expect(overview.querySelector('[data-status-group="failed"]')).toHaveTextContent("25");
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "Unique" },
    });
    expect(container.querySelector('[data-task-id="45"]')).toBeVisible();
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "2");
    await filterByStatus("success");
    expect(container.querySelector('[role="table"]')).toBeNull();
    await filterByStatus("all");
    expect(screen.getByLabelText("postParser.search.label")).toHaveValue("Unique");
    expect(container.querySelector('[data-task-id="45"]')).toBeVisible();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.filters.clear" }));
    expect(screen.getByLabelText("postParser.search.label")).toHaveValue("");
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(api.start).not.toHaveBeenCalled();
  });

  it("filters with keyboard-accessible status counters instead of a separate status select", async () => {
    usePostParserTasksStore.getState().setTasks(
      records(30).map((task, index) => ({
        ...task,
        workflowStatus: index >= 25 ? WorkflowRunStatus.Failed : WorkflowRunStatus.Success,
      })),
    );
    await show(<PostParserPage />);
    await goToPage(2);
    expect(container.querySelector('[aria-label="postParser.filters.status"]')).toBeNull();
    const failed = required(
      container.querySelector<HTMLElement>('button[data-status-group="failed"]'),
      "failed filter",
    );
    expect(failed).toHaveAttribute("aria-pressed", "false");
    failed.focus();
    await userEvent.setup().keyboard("{Enter}");
    expect(failed).toHaveAttribute("aria-pressed", "true");
    expect(container.querySelector('button[data-status-group="all"]')).toHaveAttribute(
      "aria-pressed",
      "false",
    );
    expect(container.querySelector('[data-task-id="26"]')).toBeVisible();
    expect(pageButton(1, true)).toBeInTheDocument();
    expect(failed).toHaveTextContent("5");
    expect(container.querySelector('[data-status-group="success"]')).toHaveTextContent("25");
    await filterByStatus("all");
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(container.querySelector('button[data-status-group="all"]')).toHaveAttribute(
      "aria-pressed",
      "true",
    );
  });
});

describe("reparsing all failed post tasks", () => {
  it("confirms a global failure snapshot across pages and filters, then sends one bounded batch", async () => {
    const records: PostParserTask[] = Array.from({ length: 49 }, (_, index) => ({
      ...base,
      id: index + 1,
      title: index === 0 ? "Visible completed post" : `Post ${index + 1}`,
      workflowRunId: index + 1001,
      workflowStatus: index < 26 ? WorkflowRunStatus.Success : WorkflowRunStatus.Failed,
    }));
    records.push(
      { ...base, id: 50, results: { DownloadInfo: { error: "Saved target failure" } } },
      { ...base, id: 51, error: "Failed without a run" },
      { ...base, id: 52, workflowStatus: WorkflowRunStatus.Interrupted, error: "Interrupted" },
      { ...base, id: 53, workflowStatus: WorkflowRunStatus.Waiting },
      { ...base, id: 54, workflowRunId: 1054, workflowStatus: WorkflowRunStatus.Failed },
      { ...base, id: 55, workflowStatus: WorkflowRunStatus.Failed, isDeleted: true },
      { ...base, id: 56, workflowRunId: 1056, workflowStatus: WorkflowRunStatus.Failed },
    );
    records[27].workflowStatus = WorkflowRunStatus.Cancelled;
    usePostParserTasksStore.getState().setTasks(records);
    useBTasksStore
      .getState()
      .setTasks([liveRun(1054, BTaskStatus.Running), liveRun(1056, BTaskStatus.Paused)]);
    await show(<PostParserPage />);
    await fireEvent.click(
      required(container.querySelector<HTMLElement>('[aria-label="pagination item 2"]'), "page 2"),
    );
    await filterByStatus("success");
    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "Visible completed" },
    });
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(1);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reparseFailed" }));
    expect(api.reparseFailed).not.toHaveBeenCalled();
    const confirmation = api.portal.mock.lastCall![1];
    expect(confirmation.title).toBe("postParser.reparseFailed.title");
    expect(confirmation.footer.okProps.children).toBe("postParser.reparseFailed.confirm");
    // A newly failed input after opening the dialog is outside this confirmed selection.
    await act(async () =>
      usePostParserTasksStore
        .getState()
        .updateTask({ ...records[0], workflowStatus: WorkflowRunStatus.Failed }),
    );
    api.reparseFailed.mockResolvedValue({
      code: 0,
      data: { requestedCount: 25, queuedCount: 23, skippedCount: 2, failedCount: 0, items: [] },
    });
    await act(async () => confirmation.onOk());
    expect(api.reparseFailed).toHaveBeenCalledExactlyOnceWith({
      tasks: Array.from({ length: 25 }, (_, index) => ({
        taskId: index + 27,
        revision: 3,
        workflowRunId: index < 23 ? index + 1027 : undefined,
      })),
    });
    expect(container).toHaveTextContent("postParser.reparseFailed.queued");
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
    expect(api.remove).not.toHaveBeenCalled();
    expect(api.removeAll).not.toHaveBeenCalled();
  });

  it("prevents repeated confirmation dialogs and duplicate requests while the batch is pending", async () => {
    let finish!: (value: unknown) => void;
    api.reparseFailed.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([{ ...base, error: "Failed" }]);
    await show(<PostParserPage />);
    const opener = screen.getByRole("button", { name: "postParser.action.reparseFailed" });
    act(() => {
      opener.click();
      opener.click();
    });
    await waitFor(() => expect(api.portal).toHaveBeenCalledTimes(1));
    const confirmation = api.portal.mock.lastCall![1];
    let pending!: Promise<void>;
    await act(async () => {
      pending = confirmation.onOk();
      void confirmation.onOk();
    });
    expect(api.reparseFailed).toHaveBeenCalledTimes(1);
    expect(opener).toBeDisabled();
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeDisabled();
    await act(async () => {
      finish({
        code: 0,
        data: { requestedCount: 1, queuedCount: 1, skippedCount: 0, failedCount: 0, items: [] },
      });
      await pending;
      confirmation.onDestroyed();
    });
    expect(api.reparseFailed).toHaveBeenCalledTimes(1);
  });

  it("allows cancellation without a request and disables the action when no failed tasks remain", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([{ ...base, workflowStatus: WorkflowRunStatus.Cancelled }]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reparseFailed" }));
    const confirmation = api.portal.mock.lastCall![1];
    await act(async () => confirmation.onClose());
    expect(api.reparseFailed).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "postParser.action.reparseFailed" })).toBeEnabled();
    await act(async () =>
      usePostParserTasksStore
        .getState()
        .setTasks([{ ...base, workflowStatus: WorkflowRunStatus.Success }]),
    );
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "postParser.action.reparseFailed" }),
      ).toBeDisabled(),
    );
    expect(api.reparseFailed).not.toHaveBeenCalled();
  });

  it("keeps failed-task reparsing available while unrelated parsing is being dispatched", async () => {
    usePostParserTasksStore.getState().setTasks([{ ...base, error: "Failed" }]);
    useBTasksStore.getState().setTasks([
      {
        ...liveRun(1000, BTaskStatus.Running),
        id: "ParseAllPosts",
      },
    ]);
    await show(<PostParserPage />);
    expect(screen.getByRole("button", { name: "postParser.action.queueing" })).toBeDisabled();
    const button = screen.getByRole("button", { name: "postParser.action.reparseFailed" });
    expect(button).toBeEnabled();
    await fireEvent.click(button);
    expect(api.portal.mock.lastCall![1].title).toBe("postParser.reparseFailed.title");
    expect(api.reparseFailed).not.toHaveBeenCalled();
  });
});

describe("post content purchase and partial results", () => {
  it("shows a prepared acquisition without duplicating the obsolete empty workflow guidance", async () => {
    const source = { module: "postParser", entityKind: "task", entityId: String(parsed.id) };
    api.acquisitionHistory.mockResolvedValue({
      code: 0,
      data: [{ source, tasks: [{ id: 4, phase: "ready", status: 3 }] }],
    });
    api.history.mockResolvedValue({ code: 0, data: [{ source, runCount: 0, runs: [] }] });
    usePostParserTasksStore.getState().setTasks([parsed]);
    await show(<PostParserPage />);
    await waitFor(() => expect(container).toHaveTextContent("acquire.plan.related"));
    expect(container).not.toHaveTextContent("acquire.history.notStartedHint");
    expect(container).not.toHaveTextContent("acquire.history.notStarted");
    expect(api.start).not.toHaveBeenCalled();
    expect(api.runWorkflow).not.toHaveBeenCalled();
  });

  it("renders delayed associated history in the existing task row using a batched source-object request", async () => {
    let finishHistory!: (response: unknown) => void;
    api.history.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishHistory = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([parsed, { ...parsed, id: 41 }]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const source = { module: "postParser", entityKind: "task", entityId: "14" };
    expect(api.history).toHaveBeenCalledExactlyOnceWith({
      sources: [source, { ...source, entityId: "41" }],
      pageIndex: 1,
      pageSize: 5,
      excludedTriggerKinds: ["postParser.manual"],
    });
    await act(async () =>
      finishHistory({
        code: 0,
        data: [
          {
            source,
            runCount: 10,
            runs: [
              {
                workflowName: "Delivered workflow files",
                phase: "delivered",
                completedFileCount: 10,
                deliveredDirectory: "/downloads/result",
                downloadTaskIds: [],
                reused: false,
                run: {
                  id: 673,
                  workflowDefinitionId: 9,
                  status: WorkflowRunStatus.Success,
                  startedAt: "2026-10-20",
                  inputCount: 1,
                  outputCount: 1,
                  outputPreviewTruncated: false,
                  failedItemCount: 0,
                  stepStats: [],
                },
              },
            ],
          },
        ],
      }),
    );
    expect(screen.getByRole("button", { name: "Delivered workflow files" })).toBeVisible();
    expect(screen.getByText("acquire.phase.delivered")).toBeVisible();
    expect(api.history).toHaveBeenCalledTimes(1);
  });

  it("binds distinct selected resources to independent runs and retries only the failed start", async () => {
    api.workflows.mockResolvedValue({
      code: 0,
      data: [
        {
          id: 99,
          name: "Local plan",
          triggerKind: "fs.processingPlan",
          enabled: true,
          activities: [{ id: 1, order: 0, kind: "acquisition.unpack", configJson: "{}" }],
        },
      ],
    });
    api.runWorkflow
      .mockResolvedValueOnce({ code: 0, data: { id: 201 } })
      .mockResolvedValueOnce({ code: 1, message: "Directory not found" })
      .mockResolvedValueOnce({ code: 0, data: { id: 202 } });
    const record = {
      ...base,
      results: {
        DownloadInfo: {
          resources: data.resources.map((resource, index) => ({
            ...resource,
            extraction: {
              requirement: index === 0 ? "unknown" : "required",
              steps: [],
              evidence: [],
            },
          })),
        },
      },
    };

    const onCreated = vi.fn();
    await show(<LocalProcessingModal task={record} onCreated={onCreated} />);
    await act(async () => Promise.resolve());
    expect(api.runWorkflow).not.toHaveBeenCalled();
    const boxes = Array.from(
      document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'),
    ).filter((input) => input.getAttribute("role") !== "switch");

    await fireEvent.click(boxes[0]);
    await fireEvent.click(boxes[1]);
    const pickers = screen
      .getAllByRole("button")
      .filter((button) => button.textContent?.trim() === "workflow.processing.chooseDirectory");
    await fireEvent.click(pickers[0]);
    await act(async () => api.portal.mock.lastCall![1].onSelected({ path: "/downloads/one" }));
    await fireEvent.click(pickers[1]);
    await act(async () => api.portal.mock.lastCall![1].onSelected({ path: "/downloads/two" }));
    expect(screen.getByText("workflow.processing.preview.waitForInstructions")).toBeVisible();
    await fireEvent.click(
      screen.getByRole("button", { name: "workflow.processing.startSelected" }),
    );
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(2));
    expect(api.runWorkflow.mock.calls[0]).toEqual([
      {
        taskId: 14,
        revision: 3,
        resourceIndex: 0,
        workflowDefinitionId: 99,
        files: [],
        alreadyProcessed: false,
      },
      { directory: "/downloads/one" },
    ]);
    expect(api.runWorkflow.mock.calls[1]).toEqual([
      {
        taskId: 14,
        revision: 3,
        resourceIndex: 1,
        workflowDefinitionId: 99,
        files: [],
        alreadyProcessed: false,
      },
      { directory: "/downloads/two" },
    ]);
    await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1));
    await fireEvent.click(
      screen.getByRole("button", { name: "workflow.processing.startSelected" }),
    );
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(3));
    expect(api.runWorkflow.mock.calls[2][1].directory).toBe("/downloads/two");
    await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(2));
    expect(api.manualWorkflow).not.toHaveBeenCalled();
  });
  it("keeps a result partial when paid content is still locked even if the workflow says success", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...parsed,
        workflowRunId: 37,
        workflowStatus: WorkflowRunStatus.Success,
        contentSnapshot: { locks: [{ url: "https://post.test/buy", price: 4, isBought: false }] },
      },
    ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(screen.getByText("postParser.state.partial")).toBeVisible();
    expect(byRole("button", "postParser.action.purchase")).toHaveLength(0);
    expect(byRole("button", "postParser.action.openPostToUnlock")).toHaveLength(0);
    expect(byRole("button", "postParser.action.openPost")).toHaveLength(0);
    expect(byRole("button", "postParser.action.addToAcquisition")).toHaveLength(0);
  });

  it("opens the source for manual purchase and refreshes parsing without a purchase request", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...base,
        workflowRunId: 37,
        workflowStatus: WorkflowRunStatus.Waiting,
        parsingState: "awaitingPurchase",
        contentSnapshot: { locks: [{ url: "https://post.test/buy", price: 4, isBought: false }] },
      },
    ]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: base.link! }));
    expect(api.openUrl).toHaveBeenCalledWith({ url: base.link });
    expect(api.request).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reParse" }));
    await waitFor(() => expect(api.reparse).toHaveBeenCalledWith(base.id));
    expect(api.start).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
  });

  it("orders available actions by source, analysis, download, files and task", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...parsed,
        workflowDefinitionId: 9,
        contentSnapshot: { mainHtml: "Saved post", locks: [] },
      },
    ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(
      Array.from(container.querySelectorAll("[data-operation-stage]")).map((e) =>
        e.getAttribute("data-operation-stage"),
      ),
    ).toEqual(["source", "analysis", "download", "processing", "task"]);
    expect(byRole("button", "postParser.action.purchase")).toHaveLength(0);
  });

  const unlockable: PostParserTask = {
    ...base,
    workflowRunId: 37,
    workflowStatus: WorkflowRunStatus.Waiting,
    parsingState: "possiblyExpired",
    availability: { status: "expired", evidence: ["The old link was removed."] },
    contentSnapshot: {
      locks: [
        { url: "https://post.test/buy/eligible", price: 4, isBought: false },
        { url: "https://post.test/buy/expensive", price: 80, isBought: false },
        { url: "https://post.test/buy/unknown", price: null, isBought: false },
      ],
    },
    purchaseQuote: {
      eligibleLockUrls: ["https://post.test/buy/eligible"],
      eligibleTotal: 4,
      excludedTotal: 80,
      excludedCount: 2,
      unknownPriceCount: 1,
    },
  };

  it("opens the task-scoped unified review without spending and follows queued parsing independently", async () => {
    usePostParserTasksStore.getState().setTasks([unlockable]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const button = screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" });

    act(() => {
      button.click();
      button.click();
    });
    expect(api.portal).toHaveBeenCalledOnce();
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.portal.mock.lastCall![1].scope).toEqual({ status: "all", taskIds: [14] });
    expect(api.request).not.toHaveBeenCalled();
    expect(api.confirmReview).not.toHaveBeenCalled();
    expect(button).toBeDisabled();
    await act(async () => Promise.resolve());
    await act(async () => {
      usePostParserTasksStore
        .getState()
        .setTasks([{ ...unlockable, workflowStatus: WorkflowRunStatus.Pending }]);
      useBTasksStore
        .getState()
        .setTasks([liveRun(unlockable.workflowRunId!, BTaskStatus.NotStarted)]);
    });
    expect(screen.getByRole("status", { name: "postParser.stage.queued" })).toBeVisible();
    await act(async () =>
      usePostParserTasksStore
        .getState()
        .setTasks([{ ...unlockable, workflowStatus: WorkflowRunStatus.Running }]),
    );
    await act(async () =>
      useBTasksStore
        .getState()
        .setTasks([liveRun(unlockable.workflowRunId!, BTaskStatus.Running, "extracting")]),
    );
    expect(screen.getByRole("status", { name: "postParser.stage.extracting" })).toBeVisible();
    await act(async () =>
      usePostParserTasksStore.getState().setTasks([
        {
          ...unlockable,
          workflowStatus: WorkflowRunStatus.Success,
          parsingState: "complete",
          contentSnapshot: { locks: [] },
          results: parsed.results,
          purchaseQuote: null,
        },
      ]),
    );
    await act(async () =>
      useBTasksStore
        .getState()
        .setTasks([liveRun(unlockable.workflowRunId!, BTaskStatus.Completed)]),
    );
    expect(screen.queryByRole("status", { name: "postParser.stage.extracting" })).toBeNull();
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it.each(["noExpiryReported", "restored"] as const)(
    "uses the same explicit review entry for %s posts without a direct unlock action",
    async (status) => {
      usePostParserTasksStore.getState().setTasks([
        {
          ...unlockable,
          availability: { status, evidence: [] },
          parsingState: "awaitingPurchase",
        },
      ]);
      await show(<PostParserPage />);
      await act(async () => Promise.resolve());
      expect(
        screen.queryByRole("button", { name: "postParser.action.unlockForAmount" }),
      ).toBeNull();
      expect(
        screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
      ).toBeVisible();
      expect(screen.getByRole("button", { name: "postParser.action.reParse" })).toBeVisible();
    },
  );

  it("routes an uncertain post through explicit unified review without authorizing an old row quote", async () => {
    const uncertain: PostParserTask = {
      ...unlockable,
      availability: { status: "unknown" as const, evidence: ["The saved post is inconclusive."] },
      parsingState: "awaitingPurchase",
    };
    usePostParserTasksStore.getState().setTasks([uncertain]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
    );
    expect(api.request).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.portal.mock.lastCall![1].scope).toEqual({ status: "all", taskIds: [base.id] });
    expect(api.portal.mock.lastCall![1]).not.toHaveProperty("onOk");
    expect(api.confirmReview).not.toHaveBeenCalled();
    expect(usePostParserTasksStore.getState().tasks[0].availability?.status).toBe("unknown");
  });

  it("opens the current waiting task for review without retrying, reparsing or purchasing", async () => {
    usePostParserTasksStore.getState().setTasks([unlockable]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reviewWaiting" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:task:14");
    expect(api.request).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
    expect(api.reparse).not.toHaveBeenCalled();
  });

  it("keeps link-check uncertainty separate from source purchase review", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...parsed,
        workflowStatus: WorkflowRunStatus.Success,
        parsingState: "complete",
        availability: { status: "unknown", evidence: [] },
        contentSnapshot: { locks: [] },
        results: {
          [PostParseTarget.DownloadInfo]: {
            ...data,
            resources: data.resources.map((resource) => ({
              ...resource,
              linkHealth: { status: "unknown" },
            })),
          },
        },
      },
    ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(screen.getByRole("button", { name: "acquire.plan.create" })).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "postParser.action.reviewUnlockForAmount" }),
    ).toBeNull();
    expect(container).not.toHaveTextContent("postParser.review.sourceHint");
  });

  it("moves purchase eligibility into unified review instead of row quote tooltips or direct purchase buttons", async () => {
    usePostParserTasksStore.getState().setTasks([unlockable]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(screen.queryByRole("button", { name: "postParser.action.unlockForAmount" })).toBeNull();
    expect(screen.queryByRole("button", { name: "postParser.action.noEligibleUnlock" })).toBeNull();
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
    );
    expect(api.portal.mock.lastCall![0]).toBe(ContentReviewDialog);
    expect(api.previewPurchase).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it.each(["revision", "workflowRunId"] as const)(
    "requires a fresh controller preview rather than trusting a row missing its %s",
    async (field) => {
      usePostParserTasksStore.getState().setTasks([{ ...unlockable, [field]: undefined }]);
      await show(<PostParserPage />);
      await act(async () => Promise.resolve());
      expect(
        screen.queryByRole("button", { name: "postParser.action.unlockForAmount" }),
      ).toBeNull();
      await fireEvent.click(
        screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
      );
      expect(api.portal.mock.lastCall![1].scope).toEqual({ status: "all", taskIds: [base.id] });
      expect(api.portal.mock.lastCall![1]).not.toHaveProperty("onConfirm");
      expect(api.request).not.toHaveBeenCalled();
    },
  );

  it("does not restore a row purchase shortcut even when the legacy quote says every item is eligible", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...unlockable,
        purchaseQuote: {
          ...unlockable.purchaseQuote!,
          excludedCount: 0,
          excludedTotal: 0,
          unknownPriceCount: 0,
        },
      },
    ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const user = userEvent.setup();

    await user.hover(
      screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
    );
    await act(async () => new Promise((resolve) => setTimeout(resolve, 350)));
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("opens review for an empty legacy quote without describing unknown prices as a free purchase", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...unlockable,
        purchaseQuote: { ...unlockable.purchaseQuote!, eligibleLockUrls: [], eligibleTotal: 0 },
      },
    ]);
    await show(<PostParserPage />);
    const button = screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" });
    expect(button).toBeEnabled();
    expect(screen.queryByRole("button", { name: "postParser.action.unlockForAmount" })).toBeNull();
    await fireEvent.click(button);
    expect(api.portal.mock.lastCall![1].scope).toEqual({ status: "all", taskIds: [base.id] });
    expect(api.confirmReview).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it("locks duplicate review dialogs and releases the entry on close without retrying legacy purchase APIs", async () => {
    api.request.mockResolvedValue({ code: 409, message: "The purchase quote has changed." });
    usePostParserTasksStore.getState().setTasks([unlockable]);
    await show(<PostParserPage />);
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" }),
    );
    const button = screen.getByRole("button", { name: "postParser.contentPurchaseReview.title" });
    expect(button).toBeDisabled();
    await fireEvent.click(button);
    expect(api.portal).toHaveBeenCalledOnce();
    await act(async () => api.portal.mock.lastCall![1].onClose());
    expect(screen.queryByRole("status", { name: "postParser.label.processing" })).toBeNull();
    expect(button).toBeEnabled();
    expect(api.request).not.toHaveBeenCalled();
    expect(api.confirmPurchase).not.toHaveBeenCalled();
    expect(api.confirmReview).not.toHaveBeenCalled();
  });
});

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  vi.clearAllMocks();
  for (const key of ["add", "start", "retry", "reparse", "remove", "removeAll", "options"] as const)
    api[key].mockResolvedValue({ code: 0 });
  api.import.mockResolvedValue({ code: 0, data: { resourceId: 77, created: true, leadCount: 1 } });
  api.request.mockResolvedValue({ code: 0 });
  api.reparseFailed.mockResolvedValue({
    code: 0,
    data: { requestedCount: 0, queuedCount: 0, skippedCount: 0, failedCount: 0, items: [] },
  });
  api.history.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.acquisitionHistory.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.fullFetch.mockReset();
  api.search.mockReset().mockImplementation(async (query: any) => {
    const response = await api.rows();
    const all = (response.data as PostParserTask[]).filter((task) => !task.isDeleted);
    const live = new Map(useBTasksStore.getState().tasks.map((task) => [task.id, task]));
    const classified = all.map((task) => ({
      task,
      status: getTaskDisplayStatus(
        task,
        task.workflowRunId ? live.get(`workflow.run.${task.workflowRunId}`) : undefined,
      ),
    }));
    const statusCounts = Object.fromEntries(
      taskStatusGroups.map((group) => [
        group,
        classified.filter((item) => item.status.group === group).length,
      ]),
    );
    const filtered = classified.filter(
      (item) =>
        (!query.status || query.status === "all" || item.status.group === query.status) &&
        (!query.keyword ||
          buildTaskSearchText(item.task).includes(query.keyword.trim().toLocaleLowerCase())) &&
        (!query.reasonCode || item.task.attentionReasonCodes?.includes(query.reasonCode)),
    );
    const pageSize = query.pageSize;
    let pageIndex = Math.max(
      1,
      Math.min(query.pageIndex, Math.max(1, Math.ceil(filtered.length / pageSize))),
    );
    if (query.locateTaskId) {
      const index = filtered.findIndex((item) => item.task.id === query.locateTaskId);
      if (index >= 0) pageIndex = Math.floor(index / pageSize) + 1;
    }
    return {
      code: response.code,
      data: {
        items: filtered
          .slice((pageIndex - 1) * pageSize, pageIndex * pageSize)
          .map((item) => item.task),
        pageIndex,
        pageSize,
        totalCount: filtered.length,
        globalTotal: all.length,
        statusCounts,
        attentionReasonCounts: Object.fromEntries(
          [
            ...new Set(
              classified
                .filter(
                  (item) =>
                    item.status.group === "attention" &&
                    (!query.keyword ||
                      buildTaskSearchText(item.task).includes(
                        query.keyword.trim().toLocaleLowerCase(),
                      )),
                )
                .flatMap((item) => item.task.attentionReasonCodes ?? []),
            ),
          ].map((code) => [
            code,
            classified.filter(
              (item) =>
                item.status.group === "attention" &&
                item.task.attentionReasonCodes?.includes(code) &&
                (!query.keyword ||
                  buildTaskSearchText(item.task).includes(
                    query.keyword.trim().toLocaleLowerCase(),
                  )),
            ).length,
          ]),
        ),
        runningTaskId: classified.find((item) => item.status.canLocate)?.task.id,
        startableCount: classified.filter(
          ({ task, status }) =>
            !status.processing &&
            ![
              WorkflowRunStatus.Success,
              WorkflowRunStatus.Waiting,
              WorkflowRunStatus.Running,
            ].includes(task.workflowStatus!) &&
            (!Object.keys(task.results ?? {}).length || !!task.error) &&
            (task.workflowRunId ? task.workflowStatus != null || !!task.error : true),
        ).length,
        pendingHandoffCount: all.filter((task) =>
          ["queued", "prepared"].includes(task.acquisitionHandoff?.status ?? ""),
        ).length,
        runningStages: Object.fromEntries(
          [
            ...new Set(
              classified
                .filter((item) => item.status.group === "running")
                .map((item) => item.status.stage!),
            ),
          ].map((stage) => [
            stage,
            classified.filter(
              (item) => item.status.group === "running" && item.status.stage === stage,
            ).length,
          ]),
        ),
      },
    };
  });
  api.failedSelection.mockReset().mockImplementation(async () => {
    const live = new Map(useBTasksStore.getState().tasks.map((task) => [task.id, task]));
    return {
      code: 0,
      data: {
        tasks: usePostParserTasksStore
          .getState()
          .tasks.filter(
            (task) =>
              !task.isDeleted &&
              getTaskDisplayStatus(
                task,
                task.workflowRunId ? live.get(`workflow.run.${task.workflowRunId}`) : undefined,
              ).group === "failed",
          )
          .map((task) => ({
            taskId: task.id,
            revision: task.revision,
            workflowRunId: task.workflowRunId,
          })),
      },
    };
  });
  api.previewDeletion
    .mockReset()
    .mockImplementation(
      async (scope: { keyword?: string; status: string; reasonCode?: string }) => {
        const live = new Map(useBTasksStore.getState().tasks.map((task) => [task.id, task]));
        const tasks = usePostParserTasksStore
          .getState()
          .tasks.filter(
            (task) =>
              !task.isDeleted &&
              (scope.status === "all" ||
                getTaskDisplayStatus(
                  task,
                  task.workflowRunId ? live.get(`workflow.run.${task.workflowRunId}`) : undefined,
                ).group === scope.status) &&
              (!scope.keyword ||
                buildTaskSearchText(task).includes(scope.keyword.toLocaleLowerCase())) &&
              (!scope.reasonCode || task.attentionReasonCodes?.includes(scope.reasonCode)),
          );
        return {
          code: 0,
          data: {
            scope: {
              keyword: scope.keyword ?? "",
              status: scope.status,
              ...(scope.reasonCode ? { reasonCode: scope.reasonCode } : {}),
            },
            count: tasks.length,
            tasks: tasks.map((task) => ({
              taskId: task.id,
              revision: task.revision,
              workflowRunId: task.workflowRunId ?? undefined,
            })),
          },
        };
      },
    );
  api.previewPurchase.mockReset().mockImplementation(async (scope) => ({
    code: 0,
    data: {
      scope: { ...scope, keyword: scope.keyword ?? "" },
      matchedCount: 1,
      actionableCount: 1,
      knownTotalCost: 3,
      knownTotalCostText: "3",
      currencyCode: "SP",
      currencyName: "SP币",
      skipReasonCounts: {},
      autoBuyThreshold: 10,
      minimumRemainingCoins: 1000,
      tasks: [
        {
          taskId: 14,
          revision: 3,
          workflowRunId: 100,
          lockUrls: ["https://example.com/lock/1"],
          maxTotalCost: 3,
          quoteHash: "fixed-quote",
        },
      ],
      skipped: [],
    },
  }));
  api.confirmPurchase.mockReset().mockResolvedValue({
    code: 0,
    data: { requestedCount: 1, queuedCount: 1, skippedCount: 0, failedCount: 0, items: [] },
  });
  api.confirmDeletion.mockReset().mockResolvedValue({
    code: 0,
    data: { requestedCount: 0, deletedCount: 0, skippedCount: 0, items: [] },
  });
  api.rows.mockImplementation(async () => ({
    code: 0,
    data: usePostParserTasksStore.getState().tasks,
  }));
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: api.copy.mockResolvedValue(undefined) },
  });
  usePostParserTasksStore.getState().setTasks([]);
  useBTasksStore.getState().setTasks([]);
  useSoulPlusOptionsStore.setState({
    data: { autoBuyThreshold: 0, minimumRemainingCoins: 0, maxConcurrency: 1, requestInterval: 0 },
  });
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
});

describe("post parsing input", () => {
  it("submits distinct batch URLs without starting downloads", async () => {
    await show(<AddTasksModal automaticallyParsing={false} />);
    const input = screen.getByLabelText("postParser.input.links");

    await fireEvent.change(input, {
      target: { value: "https://example.com/1\nhttps://example.com/1\nhttps://example.com/2" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.addTasks" }));
    await waitFor(() =>
      expect(api.add).toHaveBeenCalledWith({
        sourceLinksMap: {},
        targets: [PostParseTarget.DownloadInfo],
        links: ["https://example.com/1", "https://example.com/2"],
        text: undefined,
        title: undefined,
      }),
    );
    expect(api.start).not.toHaveBeenCalled();
    expect(api.import).not.toHaveBeenCalled();
  });

  it("validates URLs but retains pasted text exactly, including passwords", async () => {
    await show(<AddTasksModal automaticallyParsing={true} />);
    await fireEvent.change(screen.getByLabelText("postParser.input.links"), {
      target: { value: "not a URL" },
    });
    expect(screen.getByRole("button", { name: "postParser.action.addAndParse" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.input.text" }));
    const text = "https://pan.example/x\nCode: Ab12\nPassword: with spaces ";

    await fireEvent.change(screen.getByLabelText("postParser.input.text"), {
      target: { value: text },
    });
    await fireEvent.change(screen.getByLabelText("postParser.input.title"), {
      target: { value: "  Test title " },
    });
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.addAndParse" }));
    await waitFor(() =>
      expect(api.add).toHaveBeenCalledWith(
        expect.objectContaining({ links: [], text, title: "Test title" }),
      ),
    );
  });
});

describe("parsed links to acquisition", () => {
  it("offers current-settings catch-up for complete saved results after producer history was cleaned", async () => {
    const task: PostParserTask = {
      ...base,
      parsingState: "complete",
      workflowRunId: 101,
      error: missingParsingWorkflowRunError,
      results: { DownloadInfo: { ...data, isComplete: true } },
    };
    usePostParserTasksStore.getState().setTasks([task]);
    await show(<PostParserPage />);
    await act(async () =>
      userEvent.click(screen.getByRole("button", { name: "acquire.plan.create" })),
    );
    await act(async () =>
      userEvent.click(screen.getByRole("menuitem", { name: "postParser.catchUp.title" })),
    );
    expect(api.portal.mock.lastCall![0]).toBe(PostAcquisitionCatchUpModal);
    expect(api.portal.mock.lastCall![1].task).toMatchObject({
      id: task.id,
      revision: task.revision,
      workflowRunId: 101,
    });
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
  });

  it("retains explicit manual source selection without starting the configured catch-up flow", async () => {
    usePostParserTasksStore.getState().setTasks([parsed]);
    await show(<PostParserPage />);
    await act(async () =>
      userEvent.click(screen.getByRole("button", { name: "acquire.plan.create" })),
    );
    expect(screen.queryByRole("menuitem", { name: "postParser.catchUp.title" })).toBeNull();
    await act(async () =>
      userEvent.click(screen.getByRole("menuitem", { name: "postParser.catchUp.manual" })),
    );
    expect(api.portal.mock.lastCall![0]).toBe(CloudDownloadModal);
    expect(api.portal.mock.lastCall![1]).toMatchObject({ purpose: "plan" });
    expect(api.start).not.toHaveBeenCalled();
  });

  it("preselects only one link and saves selected indices with the preview revision", async () => {
    await show(<AddToAcquisitionModal task={parsed} />);
    const choices = screen.getAllByRole("checkbox");

    expect(choices[0]).toBeChecked();
    expect(choices[1]).not.toBeChecked();
    expect(screen.getByText(/aBc1/)).toBeVisible();
    expect(screen.getByText(/second password/)).toBeVisible();
    await fireEvent.click(choices[1]);
    await fireEvent.change(screen.getByLabelText("postParser.acquisition.title"), {
      target: { value: "New name" },
    });
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.action.addToAcquisition" }),
    );
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledWith(14, {
        title: "New name",
        resourceIndices: [0, 1],
        revision: 3,
      }),
    );
    expect(api.start).not.toHaveBeenCalled();
    await fireEvent.click(
      await screen.findByRole("button", { name: "postParser.action.openAcquisitions" }),
    );
    expect(api.navigate).toHaveBeenCalledWith("/acquisitions");
  });

  it("retains the preview and selection when a changed result is rejected", async () => {
    api.import.mockResolvedValue({
      code: 409,
      message: "The result has changed; refresh the preview.",
    });
    await show(<AddToAcquisitionModal task={parsed} />);
    await fireEvent.click(
      screen.getByRole("button", { name: "postParser.action.addToAcquisition" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("The result has changed");
    expect(screen.getAllByRole("checkbox")[0]).toBeChecked();
    expect(screen.queryByRole("button", { name: "postParser.action.openAcquisitions" })).toBeNull();
  });

  it("copies links, access codes and passwords independently", async () => {
    await show(<DownloadInfoResultRenderer data={{ resources: [data.resources[0]] }} />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyLink" }));
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyCode" }));
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPassword" }));
    await waitFor(() =>
      expect(api.copy.mock.calls).toEqual([[data.resources[0].link], ["aBc1"], ["first password"]]),
    );
  });
});

describe("post parsing workspace", () => {
  it("explains that parsing alone does not create an acquisition task", async () => {
    const source = { module: "postParser", entityKind: "task", entityId: String(base.id) };
    api.acquisitionHistory.mockResolvedValue({ code: 0, data: [{ source, tasks: [] }] });
    usePostParserTasksStore.getState().setTasks([
      {
        ...parsed,
        parsingState: "complete",
        workflowStatus: WorkflowRunStatus.Success,
      },
    ]);
    await show(<PostParserPage />);
    await waitFor(() =>
      expect(container).toHaveTextContent("postParser.history.acquisitionNextStep"),
    );
    expect(api.import).not.toHaveBeenCalled();
    expect(api.request).not.toHaveBeenCalled();
  });

  it("searches all server records through bounded pages and keeps actions on the matching record", async () => {
    const records = Array.from({ length: 200 }, (_, index) => ({
      ...base,
      id: index + 1,
      title: `Post ${index + 1}`,
      link: `https://example.com/post/${index + 1}`,
    }));

    usePostParserTasksStore.getState().setTasks(records);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(20);
    expect(container.querySelector('[data-task-id="200"]')).toBeNull();
    const initialRequests = api.rows.mock.calls.length;

    await fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "  POST 200 " },
    });
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(1);
    expect(container.querySelector('[data-task-id="200"]')).toHaveTextContent("Post 200");
    expect(api.rows).toHaveBeenCalledTimes(initialRequests + 1);
    expect(api.fullFetch).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.delete" }));
    expect(api.remove).not.toHaveBeenCalled();
    const confirmation = api.portal.mock.lastCall![1];
    expect(confirmation.title).toBe("postParser.delete.title");
    await act(async () => confirmation.onOk());
    await waitFor(() => expect(api.remove).toHaveBeenCalledWith(200));
  });

  it("searches parsed information, handles no matches and clears the filter", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...parsed, id: 15 }]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const input = screen.getByLabelText("postParser.search.label");

    await fireEvent.change(input, { target: { value: "SECOND PASSWORD" } });
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(1);
    expect(container.querySelector('[data-task-id="15"]')).toHaveTextContent("second password");
    await fireEvent.change(input, { target: { value: "does not exist" } });
    expect(screen.getByRole("status")).toHaveTextContent("postParser.search.empty");
    expect(container.querySelector('[role="table"]')).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.search.clear" }));
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeEnabled();
  });

  it("shows the missing title and parsing status together before the post details", async () => {
    usePostParserTasksStore.getState().setTasks([{ ...base, title: undefined }]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const title = screen.getByText("postParser.label.untitled");

    expect(title.parentElement).toHaveTextContent("postParser.label.pending");
    expect(
      container.querySelector('[data-task-id="14"] [role="cell"]:nth-child(3)'),
    ).not.toHaveTextContent("postParser.label.pending");
  });

  it("copies the source post link with feedback and keeps opening it available", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPostLink" }));
    await waitFor(() => expect(api.copy).toHaveBeenCalledWith(base.link));
    expect(api.copied).toHaveBeenCalledWith("postParser.result.copied");
    expect(api.openUrl).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: base.link }));
    expect(api.openUrl).toHaveBeenCalledWith({ url: base.link });
  });

  it("shows a copy failure when the clipboard and browser fallback both fail", async () => {
    const original = Object.getOwnPropertyDescriptor(document, "execCommand");

    Object.defineProperty(document, "execCommand", { configurable: true, value: () => false });
    try {
      api.copy.mockRejectedValue(new Error("Clipboard unavailable"));
      usePostParserTasksStore.getState().setTasks([base]);
      await show(<PostParserPage />);
      await fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPostLink" }));
      await waitFor(() =>
        expect(api.copyFailed).toHaveBeenCalledWith("postParser.result.copyFailed"),
      );
      expect(api.copied).not.toHaveBeenCalled();
    } finally {
      if (original) Object.defineProperty(document, "execCommand", original);
      else delete (document as Partial<Document>).execCommand;
    }
  });

  it("shows persisted creation and completion times and leaves historical times unknown", async () => {
    const createdAt = "2026-10-01T07:08:09Z";
    const completedAt = "2026-10-01T07:09:10Z";

    usePostParserTasksStore.getState().setTasks([{ ...parsed, createdAt, completedAt }]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(container.querySelector(`time[datetime="${createdAt}"]`)).toBeVisible();
    expect(container.querySelector(`time[datetime="${completedAt}"]`)).toBeVisible();
    expect(screen.getByText("postParser.label.createdAt")).toBeVisible();
    expect(screen.getByText("postParser.label.completedAt")).toBeVisible();
    expect(container.querySelector("dl")?.children).toHaveLength(2);
    await act(async () =>
      usePostParserTasksStore.getState().setTasks([{ ...base, link: "", text: "Pasted content" }]),
    );
    await waitFor(() => expect(container.querySelectorAll("time")).toHaveLength(0));
    expect(screen.queryByRole("button", { name: "postParser.action.copyPostLink" })).toBeNull();
    expect(container.querySelectorAll("dl dd")).toHaveLength(2);
    expect(Array.from(container.querySelectorAll("dl dd")).map((item) => item.textContent)).toEqual(
      ["—", "—"],
    );
    expect(container).not.toHaveTextContent("Invalid Date");
  });

  it("starts pending tasks explicitly and retains old parsed records", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...parsed, id: 15 }]);
    await show(<PostParserPage />);
    await screen.findByText("A parsed resource");
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.start" }));
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
    expect(api.import).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "acquire.plan.create" })).toBeEnabled();
  });

  it.each([WorkflowRunStatus.Failed, WorkflowRunStatus.Interrupted, WorkflowRunStatus.Cancelled])(
    "allows bulk start when only a %s execution remains",
    async (workflowStatus) => {
      usePostParserTasksStore.getState().setTasks([{ ...base, workflowRunId: 37, workflowStatus }]);
      await show(<PostParserPage />);
      const button = screen.getByRole("button", { name: "postParser.action.start" });

      expect(button).toBeEnabled();
      await fireEvent.click(button);
      await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
      expect(api.retry).not.toHaveBeenCalled();
      expect(api.reparse).not.toHaveBeenCalled();
    },
  );

  it("allows bulk start for an older failed input without a workflow run", async () => {
    usePostParserTasksStore.getState().setTasks([{ ...base, error: "Old fetch error" }]);
    await show(<PostParserPage />);
    const button = screen.getByRole("button", { name: "postParser.action.start" });

    expect(button).toBeEnabled();
    await fireEvent.click(button);
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
  });

  it("lets an orphaned failed input start in bulk or reparse individually", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([
        { ...base, workflowRunId: 37, error: "The parsing workflow run no longer exists." },
      ]);
    await show(<PostParserPage />);

    expect(screen.queryByRole("status", { name: "postParser.label.processing" })).toBeNull();
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeEnabled();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reParse" }));
    await waitFor(() => expect(api.reparse).toHaveBeenCalledWith(14));
    expect(api.retry).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeEnabled(),
    );
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.start" }));
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
  });

  it.each([
    { ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Success },
    { ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Waiting },
    { ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Waiting, error: "Needs input" },
    { ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Running },
    { ...base, workflowRunId: 37 },
    { ...base, error: "Deleted fetch error", isDeleted: true },
    parsed,
    { ...base, results: { DownloadInfo: data } },
    { ...base, results: { DownloadInfo: null } },
  ])("excludes a settled, waiting, active or deleted input from bulk start: %j", async (task) => {
    usePostParserTasksStore.getState().setTasks([task]);
    await show(<PostParserPage />);

    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeDisabled();
    expect(api.start).not.toHaveBeenCalled();
  });

  it("waits for the failed workflow's BTask to finish before allowing a bulk retry", async () => {
    const workflowTask = {
      id: "workflow.run.37",
      name: "Parse post",
      createdAt: "2026-10-05T00:00:00Z",
      isPersistent: false,
      type: BTaskType.Any,
      resourceType: BTaskResourceType.Any,
      status: BTaskStatus.Running,
    };

    useBTasksStore.getState().setTasks([workflowTask]);
    usePostParserTasksStore
      .getState()
      .setTasks([{ ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Failed }]);
    await show(<PostParserPage />);
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeDisabled();
    await act(async () =>
      useBTasksStore.getState().setTasks([{ ...workflowTask, status: BTaskStatus.Error }]),
    );
    // Server summary updates arrive independently of the immediate live row overlay.
    await act(async () => usePostParserTasksStore.getState().notifyChanged());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeEnabled(),
    );
  });

  it("retries only the failed execution and scopes history to that post", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...base,
        error: "Provider temporarily unavailable",
        workflowRunId: 37,
        workflowDefinitionId: 9,
        workflowStatus: WorkflowRunStatus.Failed,
      },
    ]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.retry" }));
    await waitFor(() => expect(api.retry).toHaveBeenCalledWith(14));
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "acquire.history.parsing" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:task:14");
  });

  it("keeps reparse for historical records without a workflow", async () => {
    usePostParserTasksStore.getState().setTasks([parsed]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.reParse" }));
    await waitFor(() => expect(api.reparse).toHaveBeenCalledWith(14));
    expect(api.start).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
  });

  it("fetches one pending post without starting another pending post", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...base, id: 15, title: "Another post" }]);
    await show(<PostParserPage />);
    await fireEvent.click(byRole("button", "postParser.action.fetchPost")[0]);
    await waitFor(() => expect(api.reparse).toHaveBeenCalledOnce());
    expect(api.reparse).toHaveBeenCalledWith(14);
    expect(api.start).not.toHaveBeenCalled();
  });

  it("guards repeated presses before React commits busy state", async () => {
    let finish!: (value: { code: number }) => void;

    api.start.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    const button = screen.getByRole("button", { name: "postParser.action.start" });

    act(() => {
      button.click();
      button.click();
    });
    expect(api.start).toHaveBeenCalledOnce();
    expect(button).toBeDisabled();
    await act(async () => {
      finish({ code: 0 });
    });
    expect(button).toBeEnabled();
  });

  it("keeps batch dispatch disabled until the BTask finishes and allows the next batch", async () => {
    const dispatcher = {
      id: "ParseAllPosts",
      name: "Queue posts",
      createdAt: "2026-10-05T00:00:00Z",
      isPersistent: true,
      type: BTaskType.Any,
      resourceType: BTaskResourceType.Any,
      status: BTaskStatus.NotStarted,
    };

    useBTasksStore.getState().setTasks([dispatcher]);
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    expect(screen.getByRole("button", { name: "postParser.action.queueing" })).toBeDisabled();
    await act(async () =>
      useBTasksStore.getState().setTasks([{ ...dispatcher, status: BTaskStatus.Completed }]),
    );
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.start" }));
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
  });

  it("shows activity for the matching workflow BTask until it finishes", async () => {
    const workflowTask = {
      id: "workflow.run.37",
      name: "Parse post",
      createdAt: "2026-10-05T00:00:00Z",
      isPersistent: false,
      type: BTaskType.Any,
      resourceType: BTaskResourceType.Any,
      status: BTaskStatus.Running,
    };

    useBTasksStore.getState().setTasks([workflowTask]);
    usePostParserTasksStore.getState().setTasks([
      { ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Waiting },
      { ...base, id: 15, workflowRunId: 38, workflowStatus: WorkflowRunStatus.Waiting },
    ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(
      container.querySelector('[data-task-id="14"] [role="status"][aria-label]'),
    ).toBeVisible();
    expect(container.querySelector('[data-task-id="15"] [role="status"][aria-label]')).toBeNull();
    await act(async () =>
      useBTasksStore.getState().setTasks([{ ...workflowTask, status: BTaskStatus.Completed }]),
    );
    expect(container.querySelector('[data-task-id="14"] [role="status"][aria-label]')).toBeNull();
  });

  it("refreshes purchase quotes when the configured price or balance limits change", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const initialRequests = api.rows.mock.calls.length;

    await act(async () =>
      useSoulPlusOptionsStore.setState({
        data: {
          autoBuyThreshold: 10,
          minimumRemainingCoins: 0,
          maxConcurrency: 1,
          requestInterval: 0,
        },
      }),
    );
    expect(api.rows).toHaveBeenCalledTimes(initialRequests + 1);
    await act(async () =>
      useSoulPlusOptionsStore.setState({
        data: {
          autoBuyThreshold: 10,
          minimumRemainingCoins: 50,
          maxConcurrency: 1,
          requestInterval: 0,
        },
      }),
    );
    expect(api.rows).toHaveBeenCalledTimes(initialRequests + 2);
    expect(api.request).not.toHaveBeenCalled();
  });

  it("keeps the search before start in the toolbar and moves automatic parsing out of it", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    const input = screen.getByLabelText("postParser.search.label");
    const start = screen.getByRole("button", { name: "postParser.action.start" });

    expect(start.parentElement).toContainElement(input);
    expect(input.compareDocumentPosition(start) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      screen.queryByRole("checkbox", { name: "postParser.label.automaticallyParsing" }),
    ).toBeNull();
  });

  it("opens all post history even when the current list is empty", async () => {
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.viewAllRuns" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:all");
  });

  it("retains per-post history access while a new revision has no linked run", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    await show(<PostParserPage />);
    await fireEvent.click(screen.getByRole("button", { name: "acquire.history.parsing" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:task:14");
  });

  it("polls active runs to their terminal state and stops after completion", async () => {
    vi.useFakeTimers();
    const running = {
      ...base,
      workflowRunId: 37,
      workflowDefinitionId: 9,
      workflowStatus: WorkflowRunStatus.Running,
    };

    useBTasksStore.getState().setTasks([liveRun(37, BTaskStatus.Running, "extracting")]);
    usePostParserTasksStore.getState().setTasks([running]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const initialCalls = api.rows.mock.calls.length;

    api.rows.mockImplementation(async () => {
      useBTasksStore.getState().setTasks([liveRun(37, BTaskStatus.Completed)]);

      return {
        code: 0,
        data: [{ ...running, results: parsed.results, workflowStatus: WorkflowRunStatus.Success }],
      };
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(api.rows.mock.calls.length).toBeGreaterThan(initialCalls);
    expect(screen.getByText("A parsed resource")).toBeVisible();
    const completedCalls = api.rows.mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(6000);
    });
    expect(api.rows).toHaveBeenCalledTimes(completedCalls);
  });
});

describe("post parser live execution stages", () => {
  it("recovers a committed pending run without a live task and prevents duplicate starts once queued", async () => {
    usePostParserTasksStore
      .getState()
      .setTasks([{ ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Pending }]);
    api.start.mockImplementation(async () => {
      useBTasksStore.getState().setTasks([liveRun(37, BTaskStatus.NotStarted)]);

      return { code: 0 };
    });
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const start = screen.getByRole("button", { name: "postParser.action.start" });

    expect(start).toBeEnabled();
    act(() => {
      start.click();
      start.click();
    });
    await waitFor(() => expect(api.start).toHaveBeenCalledExactlyOnceWith());
    expect(screen.getByRole("status", { name: "postParser.stage.queued" })).toBeVisible();
    expect(start).toBeDisabled();
  });

  it.each([
    "fetching",
    "waitingForAi",
    "checkingAvailability",
    "purchasing",
    "extracting",
    "checkingLinks",
  ])("displays the live %s stage and prevents duplicate starts", async (stage) => {
    usePostParserTasksStore
      .getState()
      .setTasks([{ ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Running }]);
    useBTasksStore.getState().setTasks([liveRun(37, BTaskStatus.Running, stage)]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(screen.getByRole("status", { name: `postParser.stage.${stage}` })).toBeVisible();
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeDisabled();
  });
  it.each([BTaskStatus.NotStarted, BTaskStatus.Paused])(
    "uses a waiting indicator for live status %s",
    async (status) => {
      usePostParserTasksStore
        .getState()
        .setTasks([{ ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Running }]);
      useBTasksStore.getState().setTasks([liveRun(37, status, "extracting")]);
      await show(<PostParserPage />);
      await act(async () => Promise.resolve());
      expect(
        screen.getByRole("status", {
          name:
            status === BTaskStatus.Paused ? "postParser.stage.paused" : "postParser.stage.queued",
        }),
      ).toBeVisible();
      expect(screen.queryByRole("status", { name: "postParser.stage.extracting" })).toBeNull();
      expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeDisabled();
    },
  );
  it.each([WorkflowRunStatus.Pending, WorkflowRunStatus.Running, WorkflowRunStatus.Interrupted])(
    "does not resurrect a persisted %s run as queued after restart",
    async (workflowStatus) => {
      usePostParserTasksStore.getState().setTasks([{ ...base, workflowRunId: 37, workflowStatus }]);
      await show(<PostParserPage />);
      await act(async () => Promise.resolve());
      expect(screen.queryByRole("status", { name: "postParser.stage.queued" })).toBeNull();
      expect(screen.queryByRole("status", { name: "postParser.label.processing" })).toBeNull();
      expect(api.start).not.toHaveBeenCalled();
    },
  );
});

describe("post parser overview and navigation", () => {
  it("preserves global counts when StrictMode renders the summary repeatedly", async () => {
    usePostParserTasksStore.getState().setTasks([
      { ...base, id: 1 },
      {
        ...base,
        id: 2,
        workflowRunId: 38,
        workflowStatus: WorkflowRunStatus.Failed,
        error: "Fetch failed",
      },
      { ...base, id: 3, workflowRunId: 39, workflowStatus: WorkflowRunStatus.Running },
    ]);
    useBTasksStore.getState().setTasks([liveRun(39, BTaskStatus.Running, "extracting")]);
    await show(
      <StrictMode>
        <PostParserPage />
      </StrictMode>,
    );
    await act(async () => Promise.resolve());
    const overview = screen.getByRole("group", { name: "postParser.summary.label" });

    expect(overview.querySelector('[data-status-group="ready"]')).toHaveTextContent(
      "postParser.summary.ready1",
    );
    expect(overview.querySelector('[data-status-group="failed"]')).toHaveTextContent(
      "postParser.summary.failed1",
    );
    expect(overview.querySelector('[data-status-group="running"]')).toHaveTextContent(
      "postParser.summary.running1",
    );
  });

  it("keeps global counts while searching and locates the first running task beyond the viewport", async () => {
    const records = Array.from(
      { length: 120 },
      (_, index): PostParserTask => ({
        ...base,
        id: index + 1,
        title: `Record ${index + 1}`,
        workflowRunId: index + 1001,
        workflowStatus: WorkflowRunStatus.Pending,
      }),
    );

    usePostParserTasksStore.getState().setTasks(records);
    useBTasksStore
      .getState()
      .setTasks([
        liveRun(1010, BTaskStatus.NotStarted),
        liveRun(1080, BTaskStatus.Running, "waitingForAi"),
        liveRun(1110, BTaskStatus.Running, "extracting"),
      ]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const overview = screen.getByRole("group", { name: "postParser.summary.label" });

    expect(overview.querySelector('[data-status-group="running"]')).toHaveTextContent(
      "postParser.summary.running2",
    );
    expect(overview.querySelector('[data-status-group="queued"]')).toHaveTextContent(
      "postParser.summary.queued1",
    );
    expect(overview.querySelector('[data-status-group="ready"]')).toHaveTextContent(
      "postParser.summary.ready117",
    );
    expect(container.querySelector('[data-task-id="80"]')).toBeNull();
    const search = screen.getByLabelText("postParser.search.label");

    await fireEvent.change(search, { target: { value: "Record 110" } });
    expect(container.querySelector('[data-task-id="110"]')).toBeVisible();
    expect(overview.querySelector('[data-status-group="running"]')).toHaveTextContent(
      "postParser.summary.running2",
    );
    await filterByStatus("failed");
    expect(container.querySelector('[role="table"]')).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.locateRunning" }));
    expect(search).toHaveValue("");
    expect(container.querySelector('[data-task-id="80"]')).toHaveAttribute("data-located", "true");
    expect(container.querySelector('[data-task-id="110"]')).toBeNull();

    // An empty search result unmounts the paged list. Remounting must not replay the old jump.
    await fireEvent.change(search, { target: { value: "No matching title" } });
    expect(container.querySelector('[role="table"]')).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.search.clear" }));
    expect(container.querySelector('[data-task-id="1"]')).toBeVisible();
    expect(container.querySelector('[data-task-id="80"]')).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "postParser.action.locateRunning" }));
    expect(container.querySelector('[data-task-id="80"]')).toHaveAttribute("data-located", "true");
  });

  it("does not count an old execution after the task moves to a new workflow run", async () => {
    usePostParserTasksStore.getState().setTasks([
      {
        ...base,
        workflowRunId: 38,
        workflowStatus: WorkflowRunStatus.Waiting,
        parsingState: "awaitingAi",
      },
    ]);
    useBTasksStore.getState().setTasks([liveRun(37, BTaskStatus.Running, "extracting")]);
    await show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const overview = screen.getByRole("group", { name: "postParser.summary.label" });

    expect(overview.querySelector('[data-status-group="running"]')).toHaveTextContent(
      "postParser.summary.running0",
    );
    expect(overview.querySelector('[data-status-group="attention"]')).toHaveTextContent(
      "postParser.summary.attention1",
    );
    expect(screen.getByRole("button", { name: "postParser.action.locateRunning" })).toBeDisabled();
  });

  it.each([BTaskStatus.NotStarted, BTaskStatus.Paused, undefined])(
    "disables navigation without admitted work (status %s)",
    async (status) => {
      usePostParserTasksStore
        .getState()
        .setTasks([{ ...base, workflowRunId: 37, workflowStatus: WorkflowRunStatus.Pending }]);
      if (status != null) useBTasksStore.getState().setTasks([liveRun(37, status, "extracting")]);
      await show(<PostParserPage />);
      await act(async () => Promise.resolve());
      expect(
        screen.getByRole("button", { name: "postParser.action.locateRunning" }),
      ).toBeDisabled();
    },
  );
});
