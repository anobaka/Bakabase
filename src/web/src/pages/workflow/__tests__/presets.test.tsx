import type { ComponentProps, ComponentType, ReactNode } from "react";
import type { components } from "@/sdk/BApi2";

import { createElement, useState } from "react";
import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent } from "@testing-library/react";

import WorkflowPage from "..";

import ManualRunModal from "@/components/Workflow/ManualRunModal";
import TemplateLibrary from "@/components/Workflow/TemplateLibrary";
import WorkflowBrowser from "@/components/Workflow/WorkflowBrowser";
import WorkflowBrowserModal from "@/components/Workflow/WorkflowBrowserModal";
import ActualSelect from "@/components/bakaui/components/Select";

type Workflow =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type Trigger =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

const api = vi.hoisted(() => ({
  search: vi.fn(),
  get: vi.fn().mockResolvedValue({ code: 0, data: { parameters: [] } }),
  triggers: vi.fn(),
  run: vi.fn(),
  add: vi.fn(),
  patch: vi.fn(),
  remove: vi.fn(),
  validate: vi.fn(),
  navigate: vi.fn(),
  portal: vi.fn(),
  baseUrl: "",
  query: "",
  paramsChanged: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    get baseUrl() {
      return api.baseUrl;
    },
    workflow: {
      searchWorkflows: api.search,
      getWorkflow: api.get,
      getWorkflowTriggers: api.triggers,
      runWorkflowManually: api.run,
      addWorkflow: api.add,
      patchWorkflow: api.patch,
      deleteWorkflow: api.remove,
      validateSavedWorkflow: api.validate,
    },
  },
}));
vi.mock("react-router-dom", () => ({
  useNavigate: () => api.navigate,
  useSearchParams: () => {
    const [params, setParams] = useState(() => new URLSearchParams(api.query));

    return [
      params,
      (next: URLSearchParams) => {
        api.paramsChanged(next);
        setParams(next);
      },
    ];
  },
}));
vi.mock("@/stores/options", () => ({ optionsStores: {} }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/HelpCenter", () => ({ HelpCenterButton: () => null }));
vi.mock("@/components/Workflow/WorkflowRunsDrawer", () => ({ default: () => null }));
vi.mock("@/components/Workflow/CanvasEditor", () => ({
  default: ({ workflow, embedded, onBack, onSaved, triggers }: any) => (
    <div data-embedded-editor={String(embedded)}>
      <span>Editing {workflow?.id ?? "new"}</span>
      <span data-editor-triggers>{triggers.map((trigger: Trigger) => trigger.kind).join(",")}</span>
      <button onClick={onBack}>Back to browser</button>
      <button onClick={() => onSaved(workflow?.id ?? 100)}>Notify definition saved</button>
    </div>
  ),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  ...(await import("@/components/bakaui/components/Tabs")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Select: (props: ComponentProps<typeof ActualSelect>) => <ActualSelect {...props} />,
  toast: { success: vi.fn(), danger: vi.fn() },
  // Keep production controls and forms; only replace modal positioning/animation.
  Modal: ({
    children,
    title,
    visible,
    footer,
    onOk,
  }: {
    children: ReactNode;
    title?: ReactNode;
    visible?: boolean;
    footer?: { actions?: string[]; okProps?: { isDisabled?: boolean } };
    onOk?: () => void;
  }) =>
    visible === false ? null : (
      <div role="dialog">
        <h2>{title}</h2>
        {children}
        {footer?.actions?.includes("ok") && (
          <button disabled={footer.okProps?.isDisabled} onClick={onOk}>
            Confirm
          </button>
        )}
      </div>
    ),
}));

const workflow = (
  id: number,
  name: string,
  triggerKind = "acquisition.requested",
  isBuiltin = true,
): Workflow => ({
  id,
  name,
  triggerKind,
  isBuiltin,
  enabled: true,
  createdAt: "2026-09-14T00:00:00Z",
  parameters: [],
  activities: [],
});
const workflows = [
  workflow(11, "Direct download", "acquisition.requested", false),
  workflow(12, "Direct download"),
  workflow(13, "Download torrent contents", "downloader.resultReady"),
  workflow(14, "Parse post download information", "postParser.manual"),
];
const triggers: Trigger[] = [
  {
    kind: "acquisition.requested",
    displayName: "Acquisition requested",
    supportsManualRun: false,
    requiresManualPayload: true,
    payloadFields: [],
    activationMode: 2,
    sourceModule: "acquisition",
  },
  {
    kind: "downloader.resultReady",
    displayName: "Download result ready",
    supportsManualRun: false,
    requiresManualPayload: true,
    payloadFields: [],
    activationMode: 2,
    sourceModule: "downloader",
  },
  {
    kind: "postParser.manual",
    displayName: "Parse post",
    supportsManualRun: true,
    requiresManualPayload: true,
    payloadFields: [],
    activationMode: 1,
    sourceModule: "postParser",
  },
];

let container: HTMLDivElement;
let root: Root;
const Harness = ({ children }: { children: ReactNode }) => {
  const [portal, setPortal] = useState<ReactNode>();

  api.portal.mockImplementation((Component: ComponentType<any>, props: object) =>
    setPortal(createElement(Component, props)),
  );

  return (
    <HeroUIProvider disableAnimation>
      {children}
      {portal}
    </HeroUIProvider>
  );
};
const show = async (content: ReactNode = <WorkflowPage />) => {
  await act(async () => root.render(<Harness>{content}</Harness>));
  await act(async () => new Promise((resolve) => setTimeout(resolve, 10)));
};
const button = (name: string, within: ParentNode = container): HTMLButtonElement => {
  const result = Array.from(within.querySelectorAll<HTMLButtonElement>("button")).find(
    (element) => (element.getAttribute("aria-label") || element.textContent?.trim()) === name,
  );

  if (!result) throw new Error(`Missing button: ${name}`);

  return result;
};
const click = async (element: HTMLElement) => {
  await act(async () => element.click());
};
const changeFilter = async (label: string, value: string) => {
  if (label === "keyword") {
    const element = container.querySelector(`[aria-label="workflow.browser.filter.${label}"]`)!;
    await act(async () => {
      fireEvent.change(element, { target: { value } });
    });
    return;
  }
  await click(button(`workflow.browser.filter.${label}`));
  const key = !value ? "all" : label === "enabled" ? value : `${label}:${value}`;
  const option = document.querySelector(`[role="option"][data-key="${key}"]`) as HTMLElement;
  expect(option).not.toBeNull();
  await click(option);
};
const visibleWorkflowIds = () =>
  [...container.querySelectorAll("[data-workflow-id]")].map((element) =>
    element.getAttribute("data-workflow-id"),
  );
const openBuiltins = () => click(button("workflow.sections.builtin"));
const noWorkflowWrites = () => {
  expect(api.run).not.toHaveBeenCalled();
  expect(api.add).not.toHaveBeenCalled();
  expect(api.patch).not.toHaveBeenCalled();
  expect(api.remove).not.toHaveBeenCalled();
};

let testId = 0;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.clearAllMocks();
  api.baseUrl = `test-${++testId}`;
  api.query = "";
  api.validate.mockReset().mockResolvedValue({ code: 0, data: { isValid: true, diagnostics: [] } });
  api.search.mockResolvedValue({ code: 0, data: workflows });
  api.triggers.mockResolvedValue({ code: 0, data: triggers });
  api.run.mockResolvedValue({ code: 0, data: 101 });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe("workflow preset entry points", () => {
  it("automatically checks configuration without duplicated check headings or execution", async () => {
    await show();
    expect(api.validate.mock.calls.map(([id]) => id)).toEqual([11, 12, 13, 14]);
    expect(container).toHaveTextContent("workflow.diagnostics.passed");
    expect(container).not.toHaveTextContent("workflow.diagnostics.title");
    expect(container.querySelector('[aria-label="workflow.diagnostics.check"]')).toBeNull();
    noWorkflowWrites();
  });

  it("includes built-in and custom definitions when a source page filters by trigger", async () => {
    api.query = "triggerKind=acquisition.requested&keep=1";
    await show();
    expect(button("workflow.sections.all")).toHaveAttribute("aria-selected", "true");
    expect(
      [...container.querySelectorAll("[data-workflow-id]")].map((element) =>
        element.getAttribute("data-workflow-id"),
      ),
    ).toEqual(["11", "12"]);
    await click(button("workflow.sections.builtin"));
    expect(
      [...container.querySelectorAll("[data-workflow-id]")].map((element) =>
        element.getAttribute("data-workflow-id"),
      ),
    ).toEqual(["12"]);
    await click(button("workflow.filter.clear"));
    expect(button("workflow.sections.custom")).toHaveAttribute("aria-selected", "true");
    expect(api.paramsChanged.mock.calls[0][0].toString()).toBe("keep=1");
    expect(button("workflow.sections.all")).toBeDefined();
    noWorkflowWrites();
  });

  it("guides invalid manual runs to configuration while keeping managed entry points available", async () => {
    api.validate.mockResolvedValue({
      code: 0,
      data: {
        isValid: false,
        diagnostics: [{ code: "missing", severity: "error", message: "Missing settings" }],
      },
    });
    await show();
    await openBuiltins();
    await click(button("workflow.diagnostics.configure"));
    expect(api.navigate).toHaveBeenLastCalledWith("/workflows/editor?id=14");
    expect(api.portal).not.toHaveBeenCalled();
    await click(button("workflow.entry.acquisition.label"));
    expect(api.navigate).toHaveBeenLastCalledWith("/acquisitions");
    noWorkflowWrites();
  });

  it("shows an unavailable check with retry instead of treating it as missing configuration", async () => {
    api.validate.mockRejectedValueOnce(new Error("offline"));
    await show();
    expect(container).toHaveTextContent("workflow.diagnostics.failed");
    expect(container).not.toHaveTextContent("workflow.diagnostics.needsAttention");
    await click(button("workflow.diagnostics.retry"));
    expect(container).toHaveTextContent("workflow.diagnostics.passed");
    noWorkflowWrites();
  });

  it("still accepts manual input when the only diagnostic depends on that input", async () => {
    api.validate.mockResolvedValue({
      code: 0,
      data: {
        isValid: false,
        diagnostics: [
          {
            code: "needsLink",
            severity: "error",
            dependsOnPayload: true,
            message: "Input link required",
          },
        ],
      },
    });
    await show();
    await openBuiltins();
    expect(container).toHaveTextContent("workflow.diagnostics.needsInput");
    await click(button("workflow.manualRun.tooltip"));
    expect(api.portal).toHaveBeenCalledWith(
      ManualRunModal,
      expect.objectContaining({ workflowId: 14 }),
    );
    expect(api.navigate).not.toHaveBeenCalled();
    noWorkflowWrites();
  });

  it("reports loading errors and lets the user retry the list", async () => {
    api.search.mockResolvedValueOnce({ code: 500, data: [] });
    await show();
    expect(container.querySelector('[role="alert"]')).toHaveTextContent("workflow.list.loadFailed");
    await click(button("workflow.diagnostics.retry"));
    expect(container).toHaveTextContent("Direct download");
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("starts with user definitions and reveals protected presets in the built-in tab", async () => {
    await show();

    expect(button("workflow.sections.custom")).toHaveAttribute("aria-selected", "true");
    expect(container.textContent).toContain("Direct download");
    expect(container.textContent).not.toContain("acquisition.recipe.directDownload");
    expect(button("workflow.action.delete")).toBeEnabled();

    await openBuiltins();

    expect(button("workflow.sections.builtin")).toHaveAttribute("aria-selected", "true");
    expect(container.textContent).not.toContain("Direct download");
    expect(container.textContent).toContain("acquisition.recipe.directDownload");
    expect(container.textContent).toContain("workflow.recipe.downloadTorrentContents.name");
    expect(container.querySelector('[aria-label="workflow.action.delete"]')).toBeNull();

    await click(button("workflow.templates.configure"));

    expect(api.navigate).toHaveBeenCalledExactlyOnceWith("/workflows/editor?id=12");
    noWorkflowWrites();
  });

  it.each([
    ["workflow.entry.acquisition.label", "/acquisitions"],
    ["workflow.entry.downloader.label", "/downloader"],
  ])("routes %s through its owning module without starting a manual run", async (label, path) => {
    await show();
    await openBuiltins();
    await click(button(label));

    expect(api.navigate).toHaveBeenCalledExactlyOnceWith(path);
    expect(api.portal).not.toHaveBeenCalled();
    noWorkflowWrites();
  });

  it("still opens the post parsing input form and waits for valid user input", async () => {
    await show();
    await openBuiltins();
    await click(button("workflow.manualRun.tooltip"));

    expect(api.portal).toHaveBeenCalledWith(
      ManualRunModal,
      expect.objectContaining({ workflowId: 14, trigger: triggers[2] }),
    );
    const dialog = container.querySelector('[role="dialog"]')!;

    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain("workflow.postParser.link");
    expect(button("Confirm", dialog)).toBeDisabled();

    await click(button("workflow.postParser.text", dialog));

    expect(dialog.querySelector("textarea")).not.toBeNull();
    expect(api.navigate).not.toHaveBeenCalled();
    noWorkflowWrites();
  });
});

describe("shared workflow browser filters and modal host", () => {
  it("uses registry sources/events, intersects category filters, and preserves unrelated URL state", async () => {
    api.query = "keep=1";
    const future = { ...workflow(51, "Future delivery", "future.received", false), enabled: false };
    api.search.mockResolvedValue({ code: 0, data: [...workflows, future] });
    api.triggers.mockResolvedValue({
      code: 0,
      data: [
        ...triggers,
        {
          kind: "future.received",
          displayName: "Future resource received",
          sourceModule: "futureModule",
          activationMode: 3,
          supportsManualRun: false,
          requiresManualPayload: false,
          payloadFields: [],
        },
      ],
    });
    await show();
    expect(visibleWorkflowIds()).toEqual(["11", "51"]);
    await changeFilter("source", "futureModule");
    expect(visibleWorkflowIds()).toEqual(["51"]);
    await click(button("workflow.browser.filter.event"));
    const events = document.querySelector('[role="listbox"]')!;
    expect(events).toHaveTextContent("Future resource received");
    expect(events).not.toHaveTextContent("Acquisition requested");
    await click(button("workflow.browser.filter.event"));
    await changeFilter("event", "future.received");
    expect(api.paramsChanged.mock.lastCall?.[0].toString()).toBe(
      "keep=1&triggerKind=future.received",
    );
    expect(button("workflow.sections.all")).toHaveAttribute("aria-selected", "true");
    await click(button("workflow.sections.builtin"));
    expect(visibleWorkflowIds()).toEqual([]);
    await click(button("workflow.sections.custom"));
    expect(visibleWorkflowIds()).toEqual(["51"]);
    noWorkflowWrites();
  });

  it("combines keyword and enabled filters without mutating or starting workflows", async () => {
    api.search.mockResolvedValue({
      code: 0,
      data: [
        ...workflows,
        { ...workflow(52, "Custom archive", "postParser.manual", false), enabled: false },
      ],
    });
    await show();
    await changeFilter("keyword", "archive");
    expect(visibleWorkflowIds()).toEqual(["52"]);
    await changeFilter("enabled", "enabled");
    expect(visibleWorkflowIds()).toEqual([]);
    await changeFilter("enabled", "disabled");
    expect(visibleWorkflowIds()).toEqual(["52"]);
    await changeFilter("keyword", "");
    expect(visibleWorkflowIds()).toEqual(["52"]);
    noWorkflowWrites();
  });

  it("keeps an unknown URL event visible and clearable while registry metadata is unavailable", async () => {
    api.query = "triggerKind=old.unknown&keep=1";
    api.triggers.mockRejectedValue(new Error("offline"));
    await show();
    const select = container.querySelector('[aria-label="workflow.browser.filter.event"]');
    expect(select).toHaveTextContent("old.unknown");
    await click(button("workflow.filter.clear"));
    expect(api.paramsChanged.mock.lastCall?.[0].toString()).toBe("keep=1");
    noWorkflowWrites();
  });

  it("shares the source-scoped all-category browser inside the large modal without routing or execution", async () => {
    const processing = [
      workflow(61, "Local preset", "fs.processingPlan"),
      workflow(62, "Local custom", "fs.processingPlan", false),
    ];
    api.search.mockResolvedValue({ code: 0, data: [...workflows, ...processing] });
    api.triggers.mockResolvedValue({
      code: 0,
      data: [
        ...triggers,
        {
          kind: "fs.processingPlan",
          displayName: "Local files",
          sourceModule: "fileSystem",
          activationMode: 1,
          supportsManualRun: true,
          requiresManualPayload: true,
          payloadFields: [],
        },
      ],
    });
    const changed = vi.fn();
    await show(
      <WorkflowBrowserModal
        visible
        triggerKind="fs.processingPlan"
        onClose={vi.fn()}
        onChanged={changed}
      />,
    );
    expect(container.querySelectorAll("[data-workflow-browser]")).toHaveLength(1);
    expect(visibleWorkflowIds()).toEqual(["61", "62"]);
    expect(button("workflow.sections.all")).toHaveAttribute("aria-selected", "true");
    expect(button("workflow.browser.filter.event")).toHaveAttribute("data-disabled", "true");
    await click(button("workflow.browser.filter.event"));
    expect(document.querySelector('[role="listbox"]')).toBeNull();
    expect(api.paramsChanged).not.toHaveBeenCalled();
    noWorkflowWrites();
    const builtin = container.querySelector('[data-workflow-id="61"]')!;
    await click(button("workflow.templates.configure", builtin));
    expect(container.querySelector('[data-embedded-editor="true"]')).not.toBeNull();
    expect(container.querySelector("[data-editor-triggers]")).toHaveTextContent(
      "fs.processingPlan",
    );
    expect(api.navigate).not.toHaveBeenCalled();
    await click(button("Back to browser"));
    expect(visibleWorkflowIds()).toEqual(["61", "62"]);
    const custom = container.querySelector('[data-workflow-id="62"]')!;
    await click(button("workflow.action.edit", custom));
    await click(button("Notify definition saved"));
    expect(changed).toHaveBeenCalledTimes(1);
    expect(visibleWorkflowIds()).toEqual(["61", "62"]);
    expect(api.navigate).not.toHaveBeenCalled();
    noWorkflowWrites();
  });

  it("allows local browser event filtering without reading or changing the workflow page URL", async () => {
    api.query = "triggerKind=unrelated.page";
    await show(<WorkflowBrowser embedded />);
    await changeFilter("event", "acquisition.requested");
    expect(visibleWorkflowIds()).toEqual(["11", "12"]);
    expect(api.paramsChanged).not.toHaveBeenCalled();
    noWorkflowWrites();
  });
});

describe("workflow template library", () => {
  it.each(["externalDownload", "fileCleaning", "manualPostDownload", "localProcessing"])(
    "opens %s for editing without creating or executing a definition",
    async (template) => {
      await show();
      await click(button("workflow.templates.title"));

      expect(api.portal).toHaveBeenCalledWith(
        TemplateLibrary,
        expect.objectContaining({ workflows }),
      );
      const heading = Array.from(container.querySelectorAll("h3")).find(
        (element) => element.textContent === `workflow.template.${template}.name`,
      )!;

      expect(heading).toBeDefined();
      await click(button("workflow.templates.configure", heading.parentElement!));

      expect(api.navigate).toHaveBeenCalledExactlyOnceWith(
        `/workflows/editor?template=${template}`,
      );
      expect(container.querySelector('[role="dialog"]')).toBeNull();
      noWorkflowWrites();
    },
  );

  it("keeps preset configuration entries without exposing user copies or duplicate legacy templates", async () => {
    const legacy = workflow(15, "Magnet");

    await show(<TemplateLibrary workflows={[...workflows, legacy]} onChoose={api.navigate} />);
    const titles = Array.from(container.querySelectorAll("h3")).map(
      (element) => element.textContent,
    );

    expect(titles).toEqual([
      "workflow.template.fileCleaning.name",
      "workflow.template.externalDownload.name",
      "workflow.template.manualPostDownload.name",
      "workflow.template.localProcessing.name",
      "acquisition.recipe.directDownload",
      "workflow.recipe.downloadTorrentContents.name",
      "workflow.recipe.postParser.name",
    ]);
    const heading = Array.from(container.querySelectorAll("h3")).find(
      (element) => element.textContent === "acquisition.recipe.directDownload",
    )!;

    await click(button("workflow.templates.configure", heading.parentElement!));

    expect(api.navigate).toHaveBeenCalledExactlyOnceWith("/workflows/editor?id=12");
    noWorkflowWrites();
  });
});
