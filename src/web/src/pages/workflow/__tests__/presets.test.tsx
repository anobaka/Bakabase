import type { ComponentType, ReactNode } from "react";
import type { components } from "@/sdk/BApi2";

import { createElement, useState } from "react";
import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowPage from "..";

import ManualRunModal from "@/components/Workflow/ManualRunModal";
import TemplateLibrary from "@/components/Workflow/TemplateLibrary";

type Workflow =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type Trigger =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

const api = vi.hoisted(() => ({
  search: vi.fn(),
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
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
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
    expect(container.querySelector('[aria-label="workflow.sections.all"]')).toBeNull();
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

describe("workflow template library", () => {
  it.each(["externalDownload", "fileCleaning"])(
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
