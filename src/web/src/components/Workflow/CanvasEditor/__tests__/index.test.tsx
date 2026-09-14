import type { ComponentProps, ReactNode } from "react";
import type { Root } from "react-dom/client";
import type { PaletteEntry } from "../NodePalette";
import type { ActivityDraft } from "../types";

import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import WorkflowCanvasEditor from "..";
import {
  EDITOR_SEED_STORAGE_KEY,
  EDITOR_TEMPLATES,
  seedToDrafts,
  takeStoredSeed,
} from "../templates";

import { WorkflowActivityErrorBehavior, WorkflowItemTypeBehavior } from "@/sdk/constants";

const {
  getWorkflowActivities,
  getWorkflowItemTypes,
  patchWorkflow,
  addWorkflow,
  validateWorkflow,
  navigate,
} = vi.hoisted(() => ({
  getWorkflowActivities: vi.fn(),
  getWorkflowItemTypes: vi.fn(),
  patchWorkflow: vi.fn(),
  addWorkflow: vi.fn(),
  navigate: vi.fn(),
  validateWorkflow: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    workflow: {
      getWorkflowActivities,
      getWorkflowItemTypes,
      patchWorkflow,
      addWorkflow,
      validateWorkflow,
    },
  },
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string; name?: string }) =>
      key === "acquisition.recipe.directDownload"
        ? "直链下载"
        : key === "workflow.editor.copyName"
          ? `${options?.name} 副本`
          : (options?.defaultValue ?? key),
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => navigate }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));

// Keep editor initialization, insertion, chain inference and UUID generation real; only
// replace configuration forms, canvas gestures and visual components at their boundaries.
vi.mock("../../Activities", () => ({
  getWorkflowActivityUI: () => ({
    defaultConfig: () => ({}),
    serializeConfig: JSON.stringify,
    parseConfig: JSON.parse,
    isValid: () => true,
    resolveAdaptedOutputType: (json: string) => JSON.parse(json).targetItemType,
  }),
}));
vi.mock("../../Triggers", () => ({
  getWorkflowTriggerUI: () => ({
    defaultFilter: () => ({}),
    parseFilter: JSON.parse,
    serializeFilter: JSON.stringify,
    resolveOutputItemType: () => "text",
  }),
}));
vi.mock("../CanvasNode", () => ({
  default: ({ draft }: { draft: ActivityDraft }) => (
    <div data-draft={JSON.stringify(draft)}>{draft.kind}</div>
  ),
}));
vi.mock("../NodePalette", () => ({
  default: ({
    entries,
    onAdd,
  }: {
    entries: PaletteEntry[];
    onAdd: (entry: PaletteEntry) => void;
  }) => (
    <div>
      {entries.map((entry) => (
        <button
          key={entry.descriptor.kind}
          disabled={!entry.fit}
          type="button"
          onClick={() => onAdd(entry)}
        >
          Add {entry.descriptor.kind}
        </button>
      ))}
    </div>
  ),
}));
vi.mock("../InspectorPanel", () => ({
  default: ({
    drafts,
    onDraftChange,
  }: {
    drafts: ActivityDraft[];
    onDraftChange: (index: number, value: ActivityDraft) => void;
  }) => (
    <button
      type="button"
      onClick={() =>
        onDraftChange(0, {
          ...drafts[0],
          configJson: '{"timeoutMinutes":120}',
          notes: "My node note",
        })
      }
    >
      Customize node
    </button>
  ),
}));
vi.mock("../../ItemTypePill", () => ({ default: () => null }));
vi.mock("../../ManualRunModal", () => ({ default: () => null }));
vi.mock("../../WorkflowRunsDrawer", () => ({ default: () => null }));
vi.mock("../useChainDrag", () => ({
  useChainDrag: () => ({ canvasRef: { current: null } }),
}));
vi.mock("../useCanvasView", () => ({
  useCanvasView: () => ({ worldRef: { current: null }, zoomPct: 100 }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    isDisabled,
    onPress,
  }: {
    children?: ReactNode;
    isDisabled?: boolean;
    onPress?: () => void;
  }) => (
    <button disabled={isDisabled} type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    value,
    isReadOnly,
    onValueChange,
  }: {
    value: string;
    isReadOnly?: boolean;
    onValueChange?: (value: string) => void;
  }) => (
    <input
      readOnly={isReadOnly}
      value={value}
      onChange={(event) => onValueChange?.(event.target.value)}
    />
  ),
  Textarea: ({
    value,
    onValueChange,
    label,
  }: {
    value: string;
    onValueChange: (value: string) => void;
    label: string;
  }) => (
    <textarea
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Switch: () => null,
  Spinner: () => <div role="status">Loading</div>,
  toast: { success: vi.fn(), danger: vi.fn() },
}));

type EditorProps = ComponentProps<typeof WorkflowCanvasEditor>;

const triggers: EditorProps["triggers"] = [
  { kind: "fs.manualScan", displayName: "Scan", requiresManualPayload: false, payloadFields: [] },
];
const aiKind = "transform.ai.transform";
const directKind = "transform.text.trim";
const bridgedKind = "action.fs.saveName";
let container: HTMLDivElement;
let root: Root;

const render = async (props: Omit<EditorProps, "triggers">) => {
  await act(async () => root.render(<WorkflowCanvasEditor {...props} triggers={triggers} />));
};

const drafts = (): ActivityDraft[] =>
  Array.from(container.querySelectorAll<HTMLElement>("[data-draft]"), (node) =>
    JSON.parse(node.dataset.draft!),
  );

const expectDistinctIds = (nodes: ActivityDraft[]) => {
  const ids = nodes.map((node) => node.clientId);

  expect(ids.length).toBeGreaterThan(0);
  expect(new Set(ids).size).toBe(ids.length);
  for (const id of ids)
    expect(id).toMatch(/^[\da-f]{8}-[\da-f]{4}-4[\da-f]{3}-[89ab][\da-f]{3}-[\da-f]{12}$/);
};

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  // LAN HTTP exposes getRandomValues but does not expose the secure-context randomUUID API.
  // Do not mock uuid: these tests exercise the package used by the editor itself.
  vi.stubGlobal("crypto", {
    getRandomValues: globalThis.crypto.getRandomValues.bind(globalThis.crypto),
  });
  getWorkflowActivities.mockResolvedValue({
    code: 0,
    data: [
      {
        kind: directKind,
        acceptedInputItemTypes: ["text"],
        outputBehavior: WorkflowItemTypeBehavior.Passthrough,
      },
      {
        kind: bridgedKind,
        acceptedInputItemTypes: ["file"],
        outputBehavior: WorkflowItemTypeBehavior.Passthrough,
      },
      {
        kind: aiKind,
        acceptedInputItemTypes: [],
        outputBehavior: WorkflowItemTypeBehavior.AdaptToNext,
      },
      {
        kind: "transform.fs.fileNameOp",
        acceptedInputItemTypes: [],
        outputBehavior: WorkflowItemTypeBehavior.Passthrough,
      },
    ],
  });
  getWorkflowItemTypes.mockResolvedValue({ code: 0, data: [] });
  patchWorkflow.mockResolvedValue({ code: 0 });
  addWorkflow.mockResolvedValue({ code: 0, data: { id: 99 } });
  validateWorkflow.mockReset();
  validateWorkflow.mockResolvedValue({ code: 0, data: { isValid: true, diagnostics: [] } });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  sessionStorage.removeItem(EDITOR_SEED_STORAGE_KEY);
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe("workflow editor without native crypto.randomUUID", () => {
  it("loads an existing recipe and inserts ordinary and AI-bridged activities", async () => {
    expect(globalThis.crypto.randomUUID).toBeUndefined();
    await render({
      workflow: {
        id: 2,
        name: "Direct download",
        triggerKind: "fs.manualScan",
        enabled: true,
        isBuiltin: true,
        createdAt: "2026-09-13T00:00:00Z",
        activities: [1, 2].map((id) => ({
          id,
          order: id - 1,
          kind: directKind,
          configJson: JSON.stringify({ existing: id }),
          onItemError: WorkflowActivityErrorBehavior.Fail,
        })),
      },
    });
    const originalDrafts = drafts();

    expect(originalDrafts).toHaveLength(2);
    expectDistinctIds(originalDrafts);
    expect(container.querySelector("input")?.value).toBe("直链下载");

    for (const kind of [directKind, bridgedKind]) {
      const button = Array.from(container.querySelectorAll("button")).find(
        (item) => item.textContent === `Add ${kind}`,
      );

      expect(button).toBeDefined();
      expect(button!.disabled).toBe(false);
      await act(async () => button!.click());
    }
    const inserted = drafts();

    expect(inserted.map((node) => node.kind)).toEqual([
      directKind,
      directKind,
      directKind,
      aiKind,
      bridgedKind,
    ]);
    expect(inserted.slice(0, 2)).toEqual(originalDrafts);
    expect(JSON.parse(inserted[3].configJson)).toEqual({ targetItemType: "file" });
    expectDistinctIds(inserted);
  });

  it("initializes a built-in template with distinct activity IDs", async () => {
    const template = EDITOR_TEMPLATES.fileCleaning;
    const seedDrafts = seedToDrafts(template);

    await render({ seed: { ...template, drafts: seedDrafts } });
    expect(drafts().map((node) => node.kind)).toEqual(template.activities.map((node) => node.kind));
    expect(drafts()).toEqual(seedDrafts);
    expectDistinctIds(drafts());
  });

  it("consumes and initializes a handoff seed while preserving its configured activities", async () => {
    const handoff = {
      name: "Rename files",
      triggerKind: "fs.manualScan",
      activities: [{ kind: directKind, configJson: '{"trimStart":true}' }],
    };

    sessionStorage.setItem(EDITOR_SEED_STORAGE_KEY, JSON.stringify(handoff));
    const seed = takeStoredSeed()!;

    await render({ seed: { ...seed, drafts: seedToDrafts(seed) } });
    expect(container.querySelector("input")?.value).toBe(handoff.name);
    expect(drafts()).toEqual([expect.objectContaining(handoff.activities[0])]);
    expectDistinctIds(drafts());
    expect(takeStoredSeed()).toBeNull();
  });
});

describe("workflow name presentation", () => {
  const definition = (name: string, isBuiltin: boolean): NonNullable<EditorProps["workflow"]> => ({
    id: 21,
    name,
    isBuiltin,
    triggerKind: "fs.manualScan",
    enabled: true,
    createdAt: "2026-09-13T00:00:00Z",
    activities: [
      {
        id: 1,
        order: 0,
        kind: directKind,
        configJson: "{}",
        onItemError: WorkflowActivityErrorBehavior.Fail,
      },
    ],
  });

  it("saves modified built-in nodes and metadata as a localized copy without patching the original", async () => {
    const workflow = {
      ...definition("Direct download", true),
      description: "Built-in workflow purpose",
    };

    await render({ workflow });
    const input = container.querySelector("input")!;

    expect(input.value).toBe("直链下载");
    expect(input.readOnly).toBe(true);
    expect(container).toHaveTextContent("workflow.editor.builtinCopyHint");
    const customize = [...container.querySelectorAll("button")].find(
      (button) => button.textContent === "Customize node",
    )!;

    await act(async () => customize.click());
    const save = [...container.querySelectorAll("button")].find(
      (button) => button.textContent === "workflow.editor.saveAsCopy",
    )!;

    expect(save).toBeEnabled();
    await act(async () => save.click());
    expect(patchWorkflow).not.toHaveBeenCalled();
    expect(addWorkflow).toHaveBeenCalledExactlyOnceWith({
      name: "直链下载 副本",
      description: "Built-in workflow purpose",
      enabled: true,
      triggerKind: workflow.triggerKind,
      triggerFilterJson: "{}",
      activities: [
        {
          kind: directKind,
          configJson: '{"timeoutMinutes":120}',
          notes: "My node note",
          onItemError: WorkflowActivityErrorBehavior.Fail,
        },
      ],
    });
    expect(navigate).toHaveBeenCalledExactlyOnceWith("/workflows/editor?id=99", { replace: true });
    expect(workflow.activities[0].configJson).toBe("{}");
    expect(workflow.name).toBe("Direct download");
  });

  it("keeps a modified built-in draft when copy creation fails and allows retry", async () => {
    addWorkflow.mockResolvedValueOnce({ code: 400, message: "Could not create the copy" });
    await render({ workflow: definition("Direct download", true) });
    const buttons = () => [...container.querySelectorAll("button")];

    await act(async () =>
      buttons()
        .find((button) => button.textContent === "Customize node")!
        .click(),
    );
    const save = buttons().find((button) => button.textContent === "workflow.editor.saveAsCopy")!;

    await act(async () => save.click());
    expect(navigate).not.toHaveBeenCalled();
    expect(patchWorkflow).not.toHaveBeenCalled();
    expect(drafts()[0]).toMatchObject({
      notes: "My node note",
      configJson: '{"timeoutMinutes":120}',
    });
    expect(save).toBeEnabled();
    await act(async () => save.click());
    expect(addWorkflow).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenCalledWith("/workflows/editor?id=99", { replace: true });
  });

  it("keeps a user workflow with a seed's name editable and saves an explicit rename", async () => {
    const workflow = definition("Direct download", false);

    await render({ workflow });
    const input = container.querySelector("input")!;

    expect(input.value).toBe("Direct download");
    expect(input.readOnly).toBe(false);
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
        input,
        "My downloads",
      );
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const save = [...container.querySelectorAll("button")].find(
      (button) => button.textContent === "workflow.editor.save",
    )!;

    await act(async () => save.click());
    expect(patchWorkflow).toHaveBeenCalledWith(
      workflow.id,
      expect.objectContaining({ name: "My downloads" }),
    );
    expect(workflow.name).toBe("Direct download");
    expect(addWorkflow).not.toHaveBeenCalled();
  });
});

describe("workflow metadata and configuration checks", () => {
  const definition = (): NonNullable<EditorProps["workflow"]> => ({
    id: 31,
    name: "My workflow",
    isBuiltin: false,
    triggerKind: "fs.manualScan",
    enabled: true,
    description: "Original purpose",
    createdAt: "2026-09-13T00:00:00Z",
    activities: [
      {
        id: 10,
        order: 0,
        kind: directKind,
        configJson: "{}",
        notes: "Keep this node note",
        onItemError: WorkflowActivityErrorBehavior.Fail,
      },
    ],
  });
  const button = (text: string) =>
    [...container.querySelectorAll("button")].find((item) => item.textContent === text)!;

  it("preserves node notes and saves an explicitly edited workflow description", async () => {
    await render({ workflow: definition() });
    const description = container.querySelector("textarea")!;

    expect(description.value).toBe("Original purpose");
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(
        description,
        "New purpose",
      );
      description.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => button("workflow.editor.save").click());
    expect(patchWorkflow).toHaveBeenCalledWith(
      31,
      expect.objectContaining({
        description: "New purpose",
        activities: [expect.objectContaining({ notes: "Keep this node note" })],
      }),
    );
  });

  it("checks draft node IDs, shows diagnostics and permits saving with missing environment configuration", async () => {
    validateWorkflow.mockResolvedValueOnce({
      code: 0,
      data: {
        isValid: false,
        diagnostics: [
          {
            nodeIndex: 0,
            code: "missingSetting",
            severity: "error",
            message: "Missing required server setting",
          },
        ],
      },
    });
    await render({ workflow: definition() });
    await act(async () => button("workflow.diagnostics.check").click());
    expect(validateWorkflow).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        triggerKind: "fs.manualScan",
        activities: [
          expect.objectContaining({
            nodeId: drafts()[0].clientId,
            notes: "Keep this node note",
            kind: directKind,
          }),
        ],
      }),
    );
    expect(container).toHaveTextContent("Missing required server setting");
    expect(button("workflow.editor.save")).toBeEnabled();
  });

  it("discards a late check after the draft changes and allows failed checks to be retried", async () => {
    let finish!: (value: unknown) => void;

    validateWorkflow.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    await render({ workflow: definition() });
    await act(async () => button("workflow.diagnostics.check").click());
    await act(async () => button(`Add ${directKind}`).click());
    await act(async () => finish({ code: 0, data: { isValid: true, diagnostics: [] } }));
    expect(container).not.toHaveTextContent("workflow.diagnostics.passed");
    expect(container).toHaveTextContent("workflow.diagnostics.unchecked");
    validateWorkflow.mockRejectedValueOnce(new Error("Offline"));
    await act(async () => button("workflow.diagnostics.check").click());
    expect(container).toHaveTextContent("workflow.diagnostics.failed");
    await act(async () => button("workflow.diagnostics.retry").click());
    expect(container).toHaveTextContent("workflow.diagnostics.passed");
  });
});
