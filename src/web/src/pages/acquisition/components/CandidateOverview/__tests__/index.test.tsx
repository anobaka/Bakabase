import type { ReactNode } from "react";
import type { Root } from "react-dom/client";

import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import CandidateOverview from "..";

import { AcquisitionLeadKind, AcquisitionStatus } from "@/sdk/constants";

const { searchCandidates, createAcquisition, createPortal, toastDanger } = vi.hoisted(() => ({
  searchCandidates: vi.fn(),
  createAcquisition: vi.fn(),
  createPortal: vi.fn(),
  toastDanger: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { acquisition: { searchAcquisitionCandidates: searchCandidates, createAcquisition } },
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));

vi.mock("@/components/Resource/components/DetailModal", () => ({ default: () => null }));

// Native controls keep disabled actions and selection changes observable while the actual
// overview owns request timing, lead/recipe selection and submission.
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    isDisabled,
    isLoading,
    onPress,
    onClick,
    type = "button",
    "aria-label": label,
  }: {
    children?: ReactNode;
    isDisabled?: boolean;
    isLoading?: boolean;
    onPress?: () => void;
    onClick?: () => void;
    type?: "button" | "submit" | "reset";
    "aria-label"?: string;
  }) => (
    <button
      aria-label={label}
      disabled={isDisabled || isLoading}
      type={type}
      onClick={onPress ?? onClick}
    >
      {children}
    </button>
  ),
  Chip: ({ children, color }: { children?: ReactNode; color?: string }) => (
    <span data-chip data-color={color}>
      {children}
    </span>
  ),
  Input: ({
    placeholder,
    value,
    onValueChange,
    label,
  }: {
    placeholder?: string;
    value?: string;
    onValueChange?: (value: string) => void;
    label?: string;
  }) => (
    <input
      aria-label={label}
      placeholder={placeholder}
      value={value}
      onChange={(event) => onValueChange?.(event.target.value)}
    />
  ),
  Select: ({
    dataSource,
    selectedKeys,
    onSelectionChange,
    label,
    isDisabled,
    "aria-label": ariaLabel,
  }: {
    dataSource: { value: string | number; label: ReactNode; disabled?: boolean }[];
    selectedKeys?: Iterable<string>;
    onSelectionChange?: (keys: Set<string>) => void;
    label?: string;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) => (
    <select
      aria-label={ariaLabel ?? label}
      disabled={isDisabled}
      value={Array.from(selectedKeys ?? [])[0] ?? ""}
      onChange={(event) => onSelectionChange?.(new Set([event.target.value]))}
    >
      <option value="">Choose</option>
      {dataSource.map((option) => (
        <option key={option.value} disabled={option.disabled} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  ),
  Pagination: ({
    page,
    total,
    onChange,
  }: {
    page: number;
    total: number;
    onChange: (page: number) => void;
  }) => (
    <nav aria-label="Pagination">
      <button disabled={page <= 1} type="button" onClick={() => onChange(page - 1)}>
        Previous page
      </button>
      <span>
        {page} / {total}
      </span>
      <button disabled={page >= total} type="button" onClick={() => onChange(page + 1)}>
        Next page
      </button>
    </nav>
  ),
  Spinner: () => <div role="status">Loading</div>,
  Tooltip: ({ children }: { children?: ReactNode }) => <>{children}</>,
  toast: { success: vi.fn(), danger: toastDanger },
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
    name: "My download recipe",
    isBuiltin: false,
    stepKinds: ["acquisition.fetchHttp"],
  },
];

const lead = (overrides = {}) => ({
  id: 11,
  kind: AcquisitionLeadKind.DirectUrl,
  value: "https://example.com/work.zip",
  sourceName: "example.com",
  isDerived: false,
  availability: "unknown",
  capability: "supported",
  method: "directDownload",
  defaultRecipeName: "Direct download",
  defaultRecipeDefinitionId: 10,
  applicableRecipeDefinitionIds: [10, 20],
  ...overrides,
});

const candidate = (overrides = {}) => ({
  resourceId: 1,
  resourceName: "Missing work",
  leads: [lead()],
  ...overrides,
});

const response = (items = [candidate()], overrides = {}) => ({
  code: 0,
  data: { items, totalCount: items.length, page: 1, pageSize: 24, recipes, ...overrides },
});

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
let onStarted: ReturnType<typeof vi.fn>;
let onViewTasks: ReturnType<typeof vi.fn>;
let onOpenRecipe: ReturnType<typeof vi.fn>;

const renderOverview = async () => {
  await act(async () =>
    root.render(
      <CandidateOverview
        onOpenRecipe={onOpenRecipe}
        onStarted={onStarted}
        onViewTasks={onViewTasks}
      />,
    ),
  );
};

function button(label: string, within: ParentNode = container) {
  const found = Array.from(within.querySelectorAll("button")).find(
    (element) =>
      element.textContent?.trim() === label || element.getAttribute("aria-label") === label,
  );

  if (!found) throw new Error(`Button not found: ${label}`);

  return found;
}

const click = async (element: HTMLElement) => {
  await act(async () => element.click());
};

async function setInput(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;

  await act(async () => {
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

async function select(input: HTMLSelectElement, value: string) {
  await act(async () => {
    input.value = value;
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.resetAllMocks();
  searchCandidates.mockResolvedValue(response());
  createAcquisition.mockResolvedValue({ code: 0, data: { id: 100 } });
  onStarted = vi.fn();
  onViewTasks = vi.fn();
  onOpenRecipe = vi.fn();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
});

describe("CandidateOverview", () => {
  it("shows an unverified source and its method without promising it is downloadable", async () => {
    await renderOverview();

    expect(container).toHaveTextContent("Missing work");
    expect(container).toHaveTextContent("example.com");
    expect(container.querySelector("[data-chip]")?.textContent).toBe(
      "acquisition.overview.unverified",
    );
    expect(container.querySelector("[data-chip]")).toHaveAttribute("data-color", "default");
    expect(container).toHaveTextContent("acquisition.overview.unverifiedDescription");
    expect(container).toHaveTextContent("acquisition.overview.method.directDownload");
    expect(container).not.toHaveTextContent("acquisition.overview.unsupported");
    expect(button("acquisition.overview.start")).toBeEnabled();
    expect(createAcquisition).not.toHaveBeenCalled();
    expect(searchCandidates).toHaveBeenCalledExactlyOnceWith({
      keyword: undefined,
      page: 1,
      pageSize: 24,
      filter: "all",
    });
  });

  it("explains a resource without sources and offers its details instead of starting a task", async () => {
    searchCandidates.mockResolvedValueOnce(response([candidate({ leads: [] })]));

    await renderOverview();

    expect(container).toHaveTextContent("acquisition.overview.noSources");
    expect(
      Array.from(container.querySelectorAll("button")).some(
        (element) => element.textContent === "acquisition.overview.start",
      ),
    ).toBe(false);
    await click(button("acquisition.overview.resourceDetails"));

    expect(createPortal).toHaveBeenCalledWith(expect.any(Function), {
      id: 1,
      onDestroyed: expect.any(Function),
    });
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it.each([
    ["unsupportedPlatform", "acquisition.overview.unsupportedPlatform"],
    ["noApplicableRecipe", "acquisition.overview.noApplicableRecipe"],
  ])("does not start a source with capability %s", async (capability, explanation) => {
    searchCandidates.mockResolvedValueOnce(
      response([
        candidate({
          leads: [
            lead({
              capability,
              applicableRecipeDefinitionIds: capability === "noApplicableRecipe" ? [] : [10, 20],
            }),
          ],
        }),
      ]),
    );

    await renderOverview();

    expect(container).toHaveTextContent(explanation);
    expect(button("acquisition.overview.start")).toBeDisabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it.each([
    {
      name: "missing default",
      defaultRecipeDefinitionId: null,
      applicableRecipeDefinitionIds: [10, 20],
    },
    {
      name: "incompatible default with one alternative",
      defaultRecipeDefinitionId: 10,
      applicableRecipeDefinitionIds: [20],
    },
  ])(
    "requires an explicit recipe choice for a $name",
    async ({ defaultRecipeDefinitionId, applicableRecipeDefinitionIds }) => {
      searchCandidates.mockResolvedValueOnce(
        response([
          candidate({
            leads: [lead({ defaultRecipeDefinitionId, applicableRecipeDefinitionIds })],
          }),
        ]),
      );

      await renderOverview();

      const selector = container.querySelector<HTMLSelectElement>(
        'select[aria-label="acquisition.overview.selectRecipe"]',
      );

      expect(selector).toBeInTheDocument();
      expect(selector).toHaveValue("");
      expect(
        Array.from(selector!.options)
          .filter((option) => option.value)
          .map((option) => Number(option.value)),
      ).toEqual(applicableRecipeDefinitionIds);
      expect(container).toHaveTextContent("acquisition.overview.defaultUnavailable");
      expect(container).toHaveTextContent("acquisition.overview.selectRecipeFirst");
      expect(container.querySelector("[data-chip]")?.textContent).toBe(
        "acquisition.overview.unverified",
      );
      expect(container).not.toHaveTextContent("acquisition.overview.unsupported");
      expect(button("acquisition.overview.start")).toBeDisabled();
      await click(button("acquisition.overview.start"));
      expect(createAcquisition).not.toHaveBeenCalled();

      await select(selector!, "20");

      expect(button("acquisition.overview.start")).toBeEnabled();
      expect(container).not.toHaveTextContent("acquisition.overview.selectRecipeFirst");
      expect(container).toHaveTextContent("acquisition.overview.method.selectedRecipe");
      await click(button("acquisition.overview.start"));

      expect(createAcquisition).toHaveBeenCalledExactlyOnceWith({
        resourceId: 1,
        acquisitionLeadId: 11,
        leadKind: AcquisitionLeadKind.DirectUrl,
        leadValue: "https://example.com/work.zip",
        recipeDefinitionId: 20,
      });
      expect(onStarted).toHaveBeenCalledTimes(1);
    },
  );

  it("starts the chosen source with that source's selected recipe", async () => {
    searchCandidates.mockResolvedValueOnce(
      response([
        candidate({
          leads: [
            lead(),
            lead({ id: 12, value: "https://mirror.example/work.zip", sourceName: "Mirror" }),
          ],
        }),
      ]),
    );

    await renderOverview();

    const recipeSelectors = container.querySelectorAll<HTMLSelectElement>(
      'select[aria-label="acquisition.overview.selectRecipe"]',
    );

    await select(recipeSelectors[1], "20");
    const starts = Array.from(container.querySelectorAll("button")).filter(
      (element) => element.textContent === "acquisition.overview.start",
    );

    await click(starts[1]);

    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith({
      resourceId: 1,
      acquisitionLeadId: 12,
      leadKind: AcquisitionLeadKind.DirectUrl,
      leadValue: "https://mirror.example/work.zip",
      recipeDefinitionId: 20,
    });
    expect(onStarted).toHaveBeenCalledTimes(1);
  });

  it("passes a derived platform source without a persisted lead id", async () => {
    searchCandidates.mockResolvedValueOnce(
      response([
        candidate({
          leads: [
            lead({
              id: 0,
              isDerived: true,
              kind: AcquisitionLeadKind.PlatformHolding,
              value: "Steam:123",
              sourceName: "Steam",
              method: "platformInstall",
            }),
          ],
        }),
      ]),
    );

    await renderOverview();
    await click(button("acquisition.overview.start"));

    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith({
      resourceId: 1,
      acquisitionLeadId: undefined,
      leadKind: AcquisitionLeadKind.PlatformHolding,
      leadValue: "Steam:123",
      recipeDefinitionId: 10,
    });
  });

  it.each([AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting])(
    "offers the existing task instead of a duplicate while status is %s",
    async (activeTaskStatus) => {
      searchCandidates.mockResolvedValueOnce(
        response([candidate({ activeTaskId: 99, activeTaskStatus })]),
      );

      await renderOverview();

      expect(button("acquisition.overview.acquiring")).toBeDisabled();
      await click(button("acquisition.overview.acquiring"));
      expect(createAcquisition).not.toHaveBeenCalled();
      await click(button("acquisition.overview.viewTask"));
      expect(onViewTasks).toHaveBeenCalledTimes(1);
    },
  );

  it("keeps search and filters on the server and resets pagination when criteria change", async () => {
    searchCandidates.mockImplementation(async ({ page }) =>
      response([candidate({ resourceName: `Page ${page}` })], { page, totalCount: 50 }),
    );

    await renderOverview();
    await click(button("Next page"));

    expect(searchCandidates).toHaveBeenLastCalledWith({
      keyword: undefined,
      page: 2,
      pageSize: 24,
      filter: "all",
    });
    expect(container).toHaveTextContent("Page 2");
    await setInput(container.querySelector("input")!, "  wanted work  ");
    expect(searchCandidates).toHaveBeenCalledTimes(2);
    await click(button("acquisition.overview.searchAction"));

    expect(searchCandidates).toHaveBeenLastCalledWith({
      keyword: "wanted work",
      page: 1,
      pageSize: 24,
      filter: "all",
    });
    await click(button("Next page"));
    await select(
      container.querySelector('select[aria-label="acquisition.overview.filter"]')!,
      "withoutSources",
    );

    expect(searchCandidates).toHaveBeenLastCalledWith({
      keyword: "wanted work",
      page: 1,
      pageSize: 24,
      filter: "withoutSources",
    });
  });

  it.each(["success", "failure"])(
    "ignores an older request's late %s after a new search",
    async (outcome) => {
      const older = deferred<ReturnType<typeof response>>();
      const newer = deferred<ReturnType<typeof response>>();

      searchCandidates.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise);
      await renderOverview();
      await setInput(container.querySelector("input")!, "new search");
      await click(button("acquisition.overview.searchAction"));
      await act(async () =>
        newer.resolve(response([candidate({ resourceName: "Current result" })])),
      );

      expect(container).toHaveTextContent("Current result");
      await act(async () => {
        if (outcome === "success")
          older.resolve(response([candidate({ resourceName: "Outdated result" })]));
        else older.reject(new Error("Old request failed"));
      });

      expect(container).toHaveTextContent("Current result");
      expect(container).not.toHaveTextContent("Outdated result");
      expect(container.querySelector('[role="alert"]')).not.toBeInTheDocument();
      expect(container.querySelector('[role="status"]')).not.toBeInTheDocument();
    },
  );

  it.each(["network", "responseCode", "missingData"])(
    "shows a retry after %s failure rather than an empty list",
    async (failure) => {
      if (failure === "network") searchCandidates.mockRejectedValueOnce(new Error("Offline"));
      else
        searchCandidates.mockResolvedValueOnce(
          failure === "responseCode" ? { code: 500 } : { code: 0 },
        );

      await renderOverview();

      expect(container.querySelector('[role="alert"]')).toHaveTextContent(
        "acquisition.overview.loadFailed",
      );
      expect(container).not.toHaveTextContent("acquisition.overview.empty");
      await click(button("acquisition.retry"));

      expect(container.querySelector('[role="alert"]')).not.toBeInTheDocument();
      expect(container).toHaveTextContent("Missing work");
      expect(searchCandidates).toHaveBeenCalledTimes(2);
    },
  );

  it("prevents duplicate starts while creating and allows retry after a failed creation", async () => {
    const creating = deferred<{ code: number }>();

    createAcquisition.mockReturnValueOnce(creating.promise);
    await renderOverview();
    await click(button("acquisition.overview.start"));

    expect(button("acquisition.overview.start")).toBeDisabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledTimes(1);
    await act(async () => creating.reject(new Error("Creation failed")));

    expect(onStarted).not.toHaveBeenCalled();
    expect(button("acquisition.overview.start")).toBeEnabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledTimes(2);
    expect(onStarted).toHaveBeenCalledTimes(1);
  });
});
