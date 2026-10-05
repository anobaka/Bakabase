import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";
import type * as ReactVirtualized from "react-virtualized";

import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PostParserPage from "..";
import AddTasksModal from "../components/AddTasksModal";
import AddToAcquisitionModal from "../components/AddToAcquisitionModal";
import DownloadInfoResultRenderer from "../components/DownloadInfoResultRenderer";
import LocalProcessingModal from "../components/LocalProcessingModal";

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

const api = vi.hoisted(() => ({
  add: vi.fn(),
  start: vi.fn(),
  retry: vi.fn(),
  reparse: vi.fn(),
  getAll: vi.fn(),
  remove: vi.fn(),
  removeAll: vi.fn(),
  import: vi.fn(),
  options: vi.fn(),
  request: vi.fn(),
  workflows: vi.fn(),
  runWorkflow: vi.fn(),
  navigate: vi.fn(),
  openUrl: vi.fn(),
  portal: vi.fn(),
  copy: vi.fn(),
  copied: vi.fn(),
  copyFailed: vi.fn(),
}));

// Keep the real virtual list; jsdom only needs its viewport dimensions supplied.
vi.mock("react-virtualized", async () => ({
  ...(await vi.importActual<typeof ReactVirtualized>("react-virtualized")),
  AutoSizer: ({ children }: { children: (size: { width: number; height: number }) => ReactNode }) =>
    children({ width: 960, height: 360 }),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    request: api.request,
    postParser: {
      addPostParserTasks: api.add,
      startAllPostParserTasks: api.start,
      retryPostParserTaskWorkflow: api.retry,
      reParsePostParserTask: api.reparse,
      getAllPostParserTasks: api.getAll,
      deletePostParserTask: api.remove,
      deleteAllPostParserTasks: api.removeAll,
      importPostParserTaskToAcquisition: api.import,
      purchasePostParserTaskContent: api.request,
    },
    options: { patchThirdPartyOptions: api.options },
    workflow: { searchWorkflows: api.workflows, runWorkflowManually: api.runWorkflow },
    gui: { openUrlInDefaultBrowser: api.openUrl },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/stores/options", () => ({
  useThirdPartyOptionsStore: (select: (state: unknown) => unknown) =>
    select({ data: { automaticallyParsingPosts: false } }),
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
  toast: { success: api.copied, danger: api.copyFailed },
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
  act(() => root.render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>));
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

  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await Promise.resolve();
    });
    try {
      assertion();

      return;
    } catch (error) {
      failure = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
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
const fireEvent = {
  click: (element: HTMLElement) => act(() => element.click()),
  change: (element: HTMLElement, event: { target: { value: string } }) =>
    act(() => {
      const prototype =
        element.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;

      Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, event.target.value);
      element.dispatchEvent(new Event("input", { bubbles: true }));
    }),
};

describe("post content purchase and partial results", () => {
  it("binds distinct selected resources to independent runs and retries only the failed start", async () => {
    api.workflows.mockResolvedValue({ code: 0, data: [{ id: 99, name: "Local plan" }] });
    api.runWorkflow
      .mockResolvedValueOnce({ code: 0, data: { id: 201 } })
      .mockResolvedValueOnce({ code: 1, message: "Directory not found" })
      .mockResolvedValueOnce({ code: 0, data: { id: 202 } });
    const record = {
      ...base,
      results: {
        DownloadInfo: {
          resources: data.resources.map((resource) => ({
            ...resource,
            extraction: { requirement: "notRequired", steps: [], evidence: [] },
          })),
        },
      },
    };

    show(<LocalProcessingModal task={record} />);
    await act(async () => Promise.resolve());
    expect(api.runWorkflow).not.toHaveBeenCalled();
    const boxes = Array.from(
      document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'),
    ).filter((input) => input.getAttribute("role") !== "switch");

    fireEvent.click(boxes[0]);
    fireEvent.click(boxes[1]);
    const directories = document.querySelectorAll<HTMLInputElement>('input[type="text"]');

    fireEvent.change(directories[0], { target: { value: "/downloads/one" } });
    fireEvent.change(directories[1], { target: { value: "/downloads/two" } });
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.startSelected" }));
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(2));
    expect(JSON.parse(api.runWorkflow.mock.calls[0][1].argsJson)).toMatchObject({
      directory: "/downloads/one",
      bindingId: "post:14:3:0",
    });
    expect(JSON.parse(api.runWorkflow.mock.calls[1][1].argsJson)).toMatchObject({
      directory: "/downloads/two",
      bindingId: "post:14:3:1",
    });
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.startSelected" }));
    await waitFor(() => expect(api.runWorkflow).toHaveBeenCalledTimes(3));
    expect(JSON.parse(api.runWorkflow.mock.calls[2][1].argsJson).directory).toBe("/downloads/two");
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
    show(<PostParserPage />);
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
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: base.link! }));
    expect(api.openUrl).toHaveBeenCalledWith({ url: base.link });
    expect(api.request).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.refreshAfterUnlock" }));
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
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(
      Array.from(container.querySelectorAll("[data-operation-stage]")).map((e) =>
        e.getAttribute("data-operation-stage"),
      ),
    ).toEqual(["source", "analysis", "download", "processing", "task"]);
    expect(byRole("button", "postParser.action.purchase")).toHaveLength(0);
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
  api.getAll.mockImplementation(async () => ({
    code: 0,
    data: usePostParserTasksStore.getState().tasks,
  }));
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: api.copy.mockResolvedValue(undefined) },
  });
  usePostParserTasksStore.getState().setTasks([]);
  useBTasksStore.getState().setTasks([]);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
});

describe("post parsing input", () => {
  it("submits distinct batch URLs without starting downloads", async () => {
    show(<AddTasksModal automaticallyParsing={false} />);
    const input = screen.getByLabelText("postParser.input.links");

    fireEvent.change(input, {
      target: { value: "https://example.com/1\nhttps://example.com/1\nhttps://example.com/2" },
    });
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addTasks" }));
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
    show(<AddTasksModal automaticallyParsing={true} />);
    fireEvent.change(screen.getByLabelText("postParser.input.links"), {
      target: { value: "not a URL" },
    });
    expect(screen.getByRole("button", { name: "postParser.action.addAndParse" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "postParser.input.text" }));
    const text = "https://pan.example/x\nCode: Ab12\nPassword: with spaces ";

    fireEvent.change(screen.getByLabelText("postParser.input.text"), { target: { value: text } });
    fireEvent.change(screen.getByLabelText("postParser.input.title"), {
      target: { value: "  Test title " },
    });
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addAndParse" }));
    await waitFor(() =>
      expect(api.add).toHaveBeenCalledWith(
        expect.objectContaining({ links: [], text, title: "Test title" }),
      ),
    );
  });
});

describe("parsed links to acquisition", () => {
  it("preselects only one link and saves selected indices with the preview revision", async () => {
    show(<AddToAcquisitionModal task={parsed} />);
    const choices = screen.getAllByRole("checkbox");

    expect(choices[0]).toBeChecked();
    expect(choices[1]).not.toBeChecked();
    expect(screen.getByText(/aBc1/)).toBeVisible();
    expect(screen.getByText(/second password/)).toBeVisible();
    fireEvent.click(choices[1]);
    fireEvent.change(screen.getByLabelText("postParser.acquisition.title"), {
      target: { value: "New name" },
    });
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    await waitFor(() =>
      expect(api.import).toHaveBeenCalledWith(14, {
        title: "New name",
        resourceIndices: [0, 1],
        revision: 3,
      }),
    );
    expect(api.start).not.toHaveBeenCalled();
    fireEvent.click(
      await screen.findByRole("button", { name: "postParser.action.openAcquisitions" }),
    );
    expect(api.navigate).toHaveBeenCalledWith("/acquisitions");
  });

  it("retains the preview and selection when a changed result is rejected", async () => {
    api.import.mockResolvedValue({
      code: 409,
      message: "The result has changed; refresh the preview.",
    });
    show(<AddToAcquisitionModal task={parsed} />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.addToAcquisition" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("The result has changed");
    expect(screen.getAllByRole("checkbox")[0]).toBeChecked();
    expect(screen.queryByRole("button", { name: "postParser.action.openAcquisitions" })).toBeNull();
  });

  it("copies links, access codes and passwords independently", async () => {
    show(<DownloadInfoResultRenderer data={{ resources: [data.resources[0]] }} />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyLink" }));
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyCode" }));
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPassword" }));
    await waitFor(() =>
      expect(api.copy.mock.calls).toEqual([[data.resources[0].link], ["aBc1"], ["first password"]]),
    );
  });
});

describe("post parsing workspace", () => {
  it("searches records outside the virtual viewport locally and keeps actions on the matching record", async () => {
    const records = Array.from({ length: 200 }, (_, index) => ({
      ...base,
      id: index + 1,
      title: `Post ${index + 1}`,
      link: `https://example.com/post/${index + 1}`,
    }));

    usePostParserTasksStore.getState().setTasks(records);
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(container.querySelectorAll("[data-task-id]").length).toBeLessThan(15);
    expect(container.querySelector('[data-task-id="200"]')).toBeNull();
    const initialRequests = api.getAll.mock.calls.length;

    fireEvent.change(screen.getByLabelText("postParser.search.label"), {
      target: { value: "  POST 200 " },
    });
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(1);
    expect(container.querySelector('[data-task-id="200"]')).toHaveTextContent("Post 200");
    expect(api.getAll).toHaveBeenCalledTimes(initialRequests);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.delete" }));
    await waitFor(() => expect(api.remove).toHaveBeenCalledWith(200));
  });

  it("searches parsed information, handles no matches and clears the filter", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...parsed, id: 15 }]);
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const input = screen.getByLabelText("postParser.search.label");

    fireEvent.change(input, { target: { value: "SECOND PASSWORD" } });
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(1);
    expect(container.querySelector('[data-task-id="15"]')).toHaveTextContent("second password");
    fireEvent.change(input, { target: { value: "does not exist" } });
    expect(screen.getByRole("status")).toHaveTextContent("postParser.search.empty");
    expect(container.querySelector('[role="table"]')).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "postParser.search.clear" }));
    expect(container.querySelectorAll("[data-task-id]")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "postParser.action.start" })).toBeEnabled();
  });

  it("shows the missing title and parsing status together before the post details", async () => {
    usePostParserTasksStore.getState().setTasks([{ ...base, title: undefined }]);
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const title = screen.getByText("postParser.label.untitled");

    expect(title.parentElement).toHaveTextContent("postParser.label.pending");
    expect(
      container.querySelector('[data-task-id="14"] [role="cell"]:nth-child(3)'),
    ).not.toHaveTextContent("postParser.label.pending");
  });

  it("copies the source post link with feedback and keeps opening it available", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPostLink" }));
    await waitFor(() => expect(api.copy).toHaveBeenCalledWith(base.link));
    expect(api.copied).toHaveBeenCalledWith("postParser.result.copied");
    expect(api.openUrl).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: base.link }));
    expect(api.openUrl).toHaveBeenCalledWith({ url: base.link });
  });

  it("shows a copy failure when the clipboard and browser fallback both fail", async () => {
    const original = Object.getOwnPropertyDescriptor(document, "execCommand");

    Object.defineProperty(document, "execCommand", { configurable: true, value: () => false });
    try {
      api.copy.mockRejectedValue(new Error("Clipboard unavailable"));
      usePostParserTasksStore.getState().setTasks([base]);
      show(<PostParserPage />);
      fireEvent.click(screen.getByRole("button", { name: "postParser.action.copyPostLink" }));
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
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    expect(container.querySelector(`time[datetime="${createdAt}"]`)).toBeVisible();
    expect(container.querySelector(`time[datetime="${completedAt}"]`)).toBeVisible();
    expect(screen.getByText("postParser.label.createdAt")).toBeVisible();
    expect(screen.getByText("postParser.label.completedAt")).toBeVisible();
    expect(container.querySelector("dl")?.children).toHaveLength(2);
    await act(async () =>
      usePostParserTasksStore.getState().setTasks([{ ...base, link: "", text: "Pasted content" }]),
    );
    expect(container.querySelectorAll("time")).toHaveLength(0);
    expect(screen.queryByRole("button", { name: "postParser.action.copyPostLink" })).toBeNull();
    expect(container.querySelectorAll("dl dd")).toHaveLength(2);
    expect(Array.from(container.querySelectorAll("dl dd")).map((item) => item.textContent)).toEqual(
      ["—", "—"],
    );
    expect(container).not.toHaveTextContent("Invalid Date");
  });

  it("starts pending tasks explicitly and retains old parsed records", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...parsed, id: 15 }]);
    show(<PostParserPage />);
    await screen.findByText("A parsed resource");
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.start" }));
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
    expect(api.import).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "postParser.action.addToAcquisition" }),
    ).toBeEnabled();
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
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.retry" }));
    await waitFor(() => expect(api.retry).toHaveBeenCalledWith(14));
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.start).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.viewRuns" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:task:14");
  });

  it("keeps reparse for historical records without a workflow", async () => {
    usePostParserTasksStore.getState().setTasks([parsed]);
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.reParse" }));
    await waitFor(() => expect(api.reparse).toHaveBeenCalledWith(14));
    expect(api.start).not.toHaveBeenCalled();
    expect(api.retry).not.toHaveBeenCalled();
  });

  it("fetches one pending post without starting another pending post", async () => {
    usePostParserTasksStore.getState().setTasks([base, { ...base, id: 15, title: "Another post" }]);
    show(<PostParserPage />);
    fireEvent.click(byRole("button", "postParser.action.fetchPost")[0]);
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
    show(<PostParserPage />);
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
    show(<PostParserPage />);
    expect(screen.getByRole("button", { name: "postParser.action.queueing" })).toBeDisabled();
    await act(async () =>
      useBTasksStore.getState().setTasks([{ ...dispatcher, status: BTaskStatus.Completed }]),
    );
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.start" }));
    await waitFor(() => expect(api.start).toHaveBeenCalledOnce());
  });

  it("keeps the search before start in the toolbar and moves automatic parsing out of it", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    show(<PostParserPage />);
    const input = screen.getByLabelText("postParser.search.label");
    const start = screen.getByRole("button", { name: "postParser.action.start" });

    expect(start.parentElement).toContainElement(input);
    expect(input.compareDocumentPosition(start) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      screen.queryByRole("checkbox", { name: "postParser.label.automaticallyParsing" }),
    ).toBeNull();
  });

  it("opens all post history even when the current list is empty", async () => {
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.viewAllRuns" }));
    expect(screen.getByTestId("runs")).toHaveTextContent("postParser:all");
  });

  it("retains per-post history access while a new revision has no linked run", async () => {
    usePostParserTasksStore.getState().setTasks([base]);
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.viewRuns" }));
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

    usePostParserTasksStore.getState().setTasks([running]);
    show(<PostParserPage />);
    await act(async () => Promise.resolve());
    const initialCalls = api.getAll.mock.calls.length;

    api.getAll.mockResolvedValue({
      code: 0,
      data: [{ ...running, results: parsed.results, workflowStatus: WorkflowRunStatus.Success }],
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(api.getAll.mock.calls.length).toBeGreaterThan(initialCalls);
    expect(screen.getByText("A parsed resource")).toBeVisible();
    const completedCalls = api.getAll.mock.calls.length;

    await act(async () => {
      await vi.advanceTimersByTimeAsync(6000);
    });
    expect(api.getAll).toHaveBeenCalledTimes(completedCalls);
  });
});
