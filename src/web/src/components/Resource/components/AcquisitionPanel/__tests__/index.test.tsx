import type { ReactNode } from "react";
import type { Root } from "react-dom/client";
import type { Resource } from "@/core/models/Resource";

import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AcquisitionPanel from "..";
import AddSourceModal from "../AddSourceModal";

import { AcquisitionLeadKind, AcquisitionStatus } from "@/sdk/constants";

const {
  getCandidate,
  createAcquisition,
  addLead,
  deleteLead,
  materialize,
  createPortal,
  navigate,
} = vi.hoisted(() => ({
  getCandidate: vi.fn(),
  createAcquisition: vi.fn(),
  addLead: vi.fn(),
  deleteLead: vi.fn(),
  materialize: vi.fn(),
  createPortal: vi.fn(),
  navigate: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: { getAcquisitionCandidate: getCandidate, createAcquisition },
    resource: {
      addResourceAcquisitionLead: addLead,
      deleteResourceAcquisitionLead: deleteLead,
      materializeResource: materialize,
    },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => navigate }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("../AddSourceModal", () => ({ default: () => null }));
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("@/components/Workflow/Triggers", () => ({ getWorkflowTriggerUI: () => undefined }));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));

// Native controls expose disabled actions while the actual panel owns its state and requests.
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    isDisabled,
    isLoading,
    onPress,
    "aria-label": label,
  }: {
    children?: ReactNode;
    isDisabled?: boolean;
    isLoading?: boolean;
    onPress?: () => void;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} disabled={isDisabled || isLoading} type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: { children?: ReactNode }) => <span>{children}</span>,
  Select: ({
    dataSource,
    selectedKeys,
    onSelectionChange,
    "aria-label": label,
  }: {
    dataSource: { value: string; label: ReactNode }[];
    selectedKeys?: Iterable<string>;
    onSelectionChange: (keys: Set<string>) => void;
    "aria-label"?: string;
  }) => (
    <select
      aria-label={label}
      value={Array.from(selectedKeys ?? [])[0] ?? ""}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="">Choose</option>
      {dataSource.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  ),
  Spinner: () => <div role="status">Loading</div>,
  Modal: () => null,
  toast: { success: vi.fn(), danger: vi.fn() },
}));

const recipes = [
  {
    definitionId: 10,
    name: "Direct download",
    isBuiltin: true,
    stepKinds: ["acquisition.fetchHttp"],
  },
  {
    definitionId: 20,
    name: "My workflow",
    isBuiltin: false,
    stepKinds: ["acquisition.waitForInbox"],
  },
];
const lead = (overrides = {}) => ({
  id: 11,
  kind: AcquisitionLeadKind.DirectUrl,
  value: "https://example.invalid/file.zip",
  isDerived: false,
  availability: "unknown",
  capability: "supported",
  method: "directDownload",
  defaultRecipeDefinitionId: 10,
  applicableRecipeDefinitionIds: [10, 20],
  ...overrides,
});
const response = (candidateOverrides = {}) => ({
  code: 0,
  data: {
    items: [
      { resourceId: 1, resourceName: "Missing work", leads: [lead()], ...candidateOverrides },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 1,
    recipes,
  },
});
const resource = (id = 1, hasLocalPath = false) => ({ id, hasLocalPath }) as Resource;

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });

  return { promise, resolve, reject };
}

let container: HTMLDivElement;
let root: Root;
let onNavigate: ReturnType<typeof vi.fn>;
const render = async (value = resource()) => {
  await act(async () => root.render(<AcquisitionPanel resource={value} onNavigate={onNavigate} />));
};
const button = (label: string) => {
  const element = Array.from(container.querySelectorAll("button")).find(
    (entry) => entry.textContent?.trim() === label || entry.getAttribute("aria-label") === label,
  );

  if (!element) throw new Error(`Button not found: ${label}`);

  return element;
};
const click = async (element: HTMLElement) => {
  await act(async () => element.click());
};
const selectRecipe = async (value: string) => {
  const selector = container.querySelector<HTMLSelectElement>(
    'select[aria-label="acquisition.overview.selectRecipe"]',
  )!;

  await act(async () => {
    selector.value = value;
    selector.dispatchEvent(new Event("change", { bubbles: true }));
  });
};

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.resetAllMocks();
  getCandidate.mockResolvedValue(response());
  createAcquisition.mockResolvedValue({ code: 0 });
  onNavigate = vi.fn();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

describe("AcquisitionPanel", () => {
  it("shows configuration problems outside collapsed details and blocks starting", async () => {
    const result = response();

    getCandidate.mockResolvedValueOnce({
      ...result,
      data: {
        ...result.data,
        recipes: recipes.map((recipe) => ({
          ...recipe,
          validation: {
            isValid: false,
            diagnostics: [
              {
                code: "missingLibrary",
                message: "Choose a library folder",
                severity: "error",
                nodeIndex: 1,
              },
            ],
          },
        })),
      },
    });
    await render();
    expect(button("acquisition.overview.start")).toBeDisabled();
    const message = [...container.querySelectorAll("p")].find(
      (element) => element.textContent === "Choose a library folder",
    );

    expect(message).toBeDefined();
    expect(message!.closest("details")).toBeNull();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it("shows the uploaded torrent filename instead of its storage reference", async () => {
    getCandidate.mockResolvedValueOnce(
      response({
        leads: [
          lead({
            kind: AcquisitionLeadKind.Torrent,
            value: `bakabase-torrent:${"a".repeat(64)}`,
            note: "demo.torrent",
          }),
        ],
      }),
    );
    await render();
    expect(container).toHaveTextContent("demo.torrent");
    expect(container).not.toHaveTextContent("bakabase-torrent:");
  });

  it("does not render or request acquisition routes for a local resource", async () => {
    await render(resource(1, true));

    expect(container).toBeEmptyDOMElement();
    expect(getCandidate).not.toHaveBeenCalled();
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it("starts a derived Steam route with its full reference and explicitly chosen workflow", async () => {
    getCandidate.mockResolvedValueOnce(
      response({
        leads: [
          lead({
            id: 0,
            isDerived: true,
            sourceName: "Steam",
            kind: AcquisitionLeadKind.PlatformHolding,
            value: "Steam:123",
            method: "platformInstall",
          }),
        ],
      }),
    );
    await render();
    await selectRecipe("20");
    await click(button("acquisition.overview.start"));

    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith({
      resourceId: 1,
      acquisitionLeadId: undefined,
      leadKind: AcquisitionLeadKind.PlatformHolding,
      leadValue: "Steam:123",
      recipeDefinitionId: 20,
    });
    expect(getCandidate).toHaveBeenCalledTimes(2);
    expect(deleteLead).not.toHaveBeenCalled();
  });

  it.each(["unsupportedPlatform", "noApplicableRecipe"])(
    "disables an unsupported route: %s",
    async (capability) => {
      getCandidate.mockResolvedValueOnce(
        response({
          leads: [
            lead({
              capability,
              applicableRecipeDefinitionIds: capability === "noApplicableRecipe" ? [] : [10, 20],
            }),
          ],
        }),
      );
      await render();

      expect(button("acquisition.overview.start")).toBeDisabled();
      expect(container).toHaveTextContent(`acquisition.overview.${capability}`);
      await click(button("acquisition.overview.start"));
      expect(createAcquisition).not.toHaveBeenCalled();
    },
  );

  it.each([
    { defaultRecipeDefinitionId: null, applicableRecipeDefinitionIds: [10, 20] },
    { defaultRecipeDefinitionId: 10, applicableRecipeDefinitionIds: [20] },
  ])("requires an explicit choice when the default is unavailable: %j", async (overrides) => {
    getCandidate.mockResolvedValueOnce(response({ leads: [lead(overrides)] }));
    await render();

    expect(container.querySelector("select")).toHaveValue("");
    expect(container).toHaveTextContent("acquisition.overview.defaultUnavailable");
    expect(button("acquisition.overview.start")).toBeDisabled();
    await selectRecipe("20");
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith({
      resourceId: 1,
      acquisitionLeadId: 11,
      leadKind: AcquisitionLeadKind.DirectUrl,
      leadValue: "https://example.invalid/file.zip",
      recipeDefinitionId: 20,
    });
  });

  it.each([AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting])(
    "prevents duplicates for an active task and closes the detail before navigation: %s",
    async (activeTaskStatus) => {
      getCandidate.mockResolvedValueOnce(response({ activeTaskId: 99, activeTaskStatus }));
      await render();

      expect(button("acquisition.overview.acquiring")).toBeDisabled();
      expect(button("acquisition.leads.chooseMethod")).toBeDisabled();
      await click(button("acquisition.overview.acquiring"));
      expect(createAcquisition).not.toHaveBeenCalled();
      await click(button("acquisition.overview.viewTask"));
      expect(onNavigate).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledExactlyOnceWith("/acquisitions?tab=live");
      expect(onNavigate.mock.invocationCallOrder[0]).toBeLessThan(
        navigate.mock.invocationCallOrder[0],
      );
    },
  );

  it.each(["network", "responseCode", "missingData"])(
    "offers retry after a %s failure instead of saying there are no routes",
    async (failure) => {
      if (failure === "network") getCandidate.mockRejectedValueOnce(new Error("Offline"));
      else
        getCandidate.mockResolvedValueOnce(
          failure === "responseCode" ? { code: 500 } : { code: 0 },
        );
      await render();

      expect(container.querySelector('[role="alert"]')).toHaveTextContent(
        "acquisition.leads.loadFailed",
      );
      expect(container).not.toHaveTextContent("acquisition.leads.empty");
      expect(button("acquisition.leads.chooseMethod")).toBeDisabled();
      await click(button("acquisition.retry"));
      expect(container.querySelector('[role="alert"]')).not.toBeInTheDocument();
      expect(button("acquisition.overview.start")).toBeEnabled();
      expect(getCandidate).toHaveBeenCalledTimes(2);
    },
  );

  it("opens the method picker without saving or acquiring, and reloads after a source is added", async () => {
    getCandidate.mockResolvedValueOnce(response({ leads: [] }));
    await render();
    await click(button("acquisition.leads.chooseMethod"));

    expect(createPortal).toHaveBeenCalledExactlyOnceWith(AddSourceModal, {
      resourceId: 1,
      onAdded: expect.any(Function),
    });
    expect(addLead).not.toHaveBeenCalled();
    expect(createAcquisition).not.toHaveBeenCalled();
    expect(materialize).not.toHaveBeenCalled();
    await act(async () => createPortal.mock.calls[0][1].onAdded());
    expect(getCandidate).toHaveBeenCalledTimes(2);
  });

  it.each(["success", "failure"])(
    "ignores an older resource request's late %s",
    async (outcome) => {
      const older = deferred<ReturnType<typeof response>>();
      const newer = deferred<ReturnType<typeof response>>();

      getCandidate.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise);
      await render(resource(1));
      await render(resource(2));
      await act(async () =>
        newer.resolve(
          response({ resourceId: 2, leads: [lead({ value: "https://new.invalid/file.zip" })] }),
        ),
      );
      await act(async () => {
        if (outcome === "success") older.resolve(response());
        else older.reject(new Error("Old request failed"));
      });

      expect(container).toHaveTextContent("https://new.invalid/file.zip");
      expect(container).not.toHaveTextContent("https://example.invalid/file.zip");
      expect(container.querySelector('[role="alert"]')).not.toBeInTheDocument();
      expect(container.querySelector('[role="status"]')).not.toBeInTheDocument();
      await click(button("acquisition.overview.start"));
      expect(createAcquisition).toHaveBeenCalledWith(
        expect.objectContaining({ resourceId: 2, leadValue: "https://new.invalid/file.zip" }),
      );
    },
  );

  it("blocks a duplicate while creating and permits retry after creation fails", async () => {
    const pending = deferred<{ code: number }>();

    createAcquisition.mockReturnValueOnce(pending.promise);
    await render();
    await click(button("acquisition.overview.start"));
    expect(button("acquisition.overview.start")).toBeDisabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledTimes(1);
    await act(async () => pending.reject(new Error("Failed")));
    expect(button("acquisition.overview.start")).toBeEnabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledTimes(2);
  });
  it("closes the detail before opening the selected workflow", async () => {
    await render();
    await selectRecipe("20");
    await click(container.querySelector("summary")!);
    await click(button("acquisition.recipes.open"));

    expect(onNavigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledExactlyOnceWith("/workflows/editor?id=20");
    expect(onNavigate.mock.invocationCallOrder[0]).toBeLessThan(
      navigate.mock.invocationCallOrder[0],
    );
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it("does not reuse another resource's explicit workflow selection", async () => {
    getCandidate.mockImplementation(async (id) => response({ resourceId: id }));
    await render(resource(1));
    await selectRecipe("20");
    await render(resource(2));

    expect(container.querySelector("select")).toHaveValue("10");
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledWith(
      expect.objectContaining({ resourceId: 2, recipeDefinitionId: 10 }),
    );
  });
});
