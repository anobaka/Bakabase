import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PostParserPage from "..";
import AddTasksModal from "../components/AddTasksModal";
import AddToAcquisitionModal from "../components/AddToAcquisitionModal";
import DownloadInfoResultRenderer from "../components/DownloadInfoResultRenderer";

import { PostParseTarget, PostParserSource, WorkflowRunStatus } from "@/sdk/constants";
import { usePostParserTasksStore } from "@/stores/postParserTasks";

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
  navigate: vi.fn(),
  openUrl: vi.fn(),
  portal: vi.fn(),
  copy: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: {
      addPostParserTasks: api.add,
      startAllPostParserTasks: api.start,
      retryPostParserTaskWorkflow: api.retry,
      reParsePostParserTask: api.reparse,
      getAllPostParserTasks: api.getAll,
      deletePostParserTask: api.remove,
      deleteAllPostParserTasks: api.removeAll,
      importPostParserTaskToAcquisition: api.import,
    },
    options: { patchThirdPartyOptions: api.options },
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
vi.mock("@/components/ThirdPartyConfig/base/TampermonkeyInstallButton", () => ({
  default: () => null,
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <span>SoulPlus</span> }));
vi.mock("@/components/Workflow/WorkflowRunsDrawer", () => ({
  default: ({ workflowDefinitionId }: { workflowDefinitionId: number }) => (
    <div data-testid="runs">{workflowDefinitionId}</div>
  ),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast: { success: vi.fn(), danger: vi.fn() },
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

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  vi.clearAllMocks();
  for (const key of ["add", "start", "retry", "reparse", "remove", "removeAll", "options"] as const)
    api[key].mockResolvedValue({ code: 0 });
  api.import.mockResolvedValue({ code: 0, data: { resourceId: 77, created: true, leadCount: 1 } });
  api.getAll.mockImplementation(async () => ({
    code: 0,
    data: usePostParserTasksStore.getState().tasks,
  }));
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText: api.copy.mockResolvedValue(undefined) },
  });
  usePostParserTasksStore.getState().setTasks([]);
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

  it("retries failed execution without reparsing all content and shows shared execution history", async () => {
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
    expect(screen.getByTestId("runs")).toHaveTextContent("9");
  });

  it("keeps reparse for historical records without a workflow", async () => {
    usePostParserTasksStore.getState().setTasks([parsed]);
    show(<PostParserPage />);
    fireEvent.click(screen.getByRole("button", { name: "postParser.action.reParse" }));
    await waitFor(() => expect(api.reparse).toHaveBeenCalledWith(14));
    expect(api.start).toHaveBeenCalledOnce();
    expect(api.retry).not.toHaveBeenCalled();
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
