import type { ReactNode } from "react";
import type { Root } from "react-dom/client";

import { createRoot } from "react-dom/client";
import { createPortal as createReactPortal } from "react-dom";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Button as HeroButton } from "@heroui/button";

import CandidateOverview, { CandidateCard } from "..";

import AddSourceModal from "@/components/Resource/components/AcquisitionPanel/AddSourceModal";
import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import { AcquisitionLeadKind, AcquisitionStatus } from "@/sdk/constants";

const { searchCandidates, createAcquisition, createPortal, toastDanger, controls } = vi.hoisted(
  () => ({
    searchCandidates: vi.fn(),
    createAcquisition: vi.fn(),
    createPortal: vi.fn(),
    toastDanger: vi.fn(),
    controls: { realSelect: false },
  }),
);

// Only activity names are used here; the editor's configuration forms stay outside this test.
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("@/components/Workflow/Triggers", () => ({ getWorkflowTriggerUI: () => undefined }));

vi.mock("@/sdk/BApi", () => ({
  default: { acquisition: { searchAcquisitionCandidates: searchCandidates, createAcquisition } },
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));

vi.mock("@/components/Resource/components/DetailModal", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/AcquisitionPanel/AddSourceModal", () => ({
  default: () => null,
}));

// The real installed Card and Button preserve HeroUI/usePress keyboard and event bubbling
// behavior. Other native controls keep request/selection tests compact.
vi.mock("@/components/bakaui", async () => {
  const { Card, CardHeader, CardBody } = await import("@heroui/card");
  const { Button } = await import("@heroui/button");
  const { Select: HeroSelect, SelectItem } = await import("@heroui/select");

  return {
    Card,
    CardHeader,
    CardBody,
    Button,
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
      "aria-label": ariaLabel,
    }: {
      placeholder?: string;
      value?: string;
      onValueChange?: (value: string) => void;
      label?: string;
      "aria-label"?: string;
    }) => (
      <input
        aria-label={ariaLabel ?? label}
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
    }) =>
      controls.realSelect ? (
        <HeroSelect
          disableAnimation
          aria-label={ariaLabel ?? label}
          isDisabled={isDisabled}
          selectedKeys={selectedKeys}
          onSelectionChange={(keys) => {
            if (keys !== "all") onSelectionChange?.(new Set([...keys].map(String)));
          }}
        >
          {dataSource.map((option) => (
            <SelectItem key={option.value} isDisabled={option.disabled}>
              {option.label}
            </SelectItem>
          ))}
        </HeroSelect>
      ) : (
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
  };
});

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

const pointerPress = async (element: HTMLElement) => {
  await act(async () => {
    if (typeof PointerEvent !== "undefined") {
      const init = {
        bubbles: true,
        cancelable: true,
        pointerId: 1,
        pointerType: "mouse",
        button: 0,
      };

      element.dispatchEvent(new PointerEvent("pointerdown", init));
      element.dispatchEvent(new PointerEvent("pointerup", init));
    } else {
      element.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, button: 0 }));
      element.dispatchEvent(new MouseEvent("mouseup", { bubbles: true, button: 0 }));
    }
    element.dispatchEvent(
      new MouseEvent("click", { bubbles: true, cancelable: true, button: 0, detail: 1 }),
    );
  });
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
  vi.clearAllMocks();
  searchCandidates.mockReset();
  createAcquisition.mockReset();
  controls.realSelect = false;
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
  vi.unstubAllGlobals();
});

describe("CandidateOverview", () => {
  it("does not treat a nested HeroUI virtual press that continues propagation as a card press", async () => {
    const cardPress = vi.fn();
    const childPress = vi.fn();

    await act(async () =>
      root.render(
        <CandidateCard isPressable as="section" onPress={cardPress}>
          <HeroButton
            onPress={(event) => {
              childPress();
              event.continuePropagation();
            }}
          >
            Nested action
          </HeroButton>
        </CandidateCard>,
      ),
    );
    await click(button("Nested action"));
    expect(childPress).toHaveBeenCalledTimes(1);
    expect(cardPress).not.toHaveBeenCalled();
    await click(container.querySelector("section")!);
    expect(cardPress).toHaveBeenCalledTimes(1);
  });

  it.each(["Enter", " "])(
    "isolates forwarded child keyboard presses and allows the next card press with %j",
    async (key) => {
      const cardPress = vi.fn();
      const childPress = vi.fn();

      await act(async () =>
        root.render(
          <CandidateCard isPressable as="section" onPress={cardPress}>
            <HeroButton
              onPress={(event) => {
                childPress();
                event.continuePropagation();
              }}
              onPressEnd={(event) => event.continuePropagation()}
              onPressStart={(event) => event.continuePropagation()}
            >
              Forwarded action
            </HeroButton>
          </CandidateCard>,
        ),
      );
      const child = button("Forwarded action");
      const card = container.querySelector("section")!;
      const pressKey = async (target: HTMLElement) =>
        act(async () => {
          target.focus();
          target.dispatchEvent(
            new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }),
          );
          target.dispatchEvent(
            new KeyboardEvent("keyup", { key, bubbles: true, cancelable: true }),
          );
        });

      await pressKey(child);
      expect(childPress).toHaveBeenCalledTimes(1);
      expect(cardPress).not.toHaveBeenCalled();
      await pressKey(card);
      expect(cardPress).toHaveBeenCalledTimes(1);
    },
  );

  it("ignores clicks from disabled nested button icons without locking the card", async () => {
    const cardPress = vi.fn();
    const childPress = vi.fn();

    await act(async () =>
      root.render(
        <CandidateCard isPressable as="section" onPress={cardPress}>
          <HeroButton isDisabled onPress={childPress}>
            Disabled action{" "}
            <svg aria-hidden>
              <path />
            </svg>
          </HeroButton>
        </CandidateCard>,
      ),
    );
    await act(async () => {
      button("Disabled action")
        .querySelector("path")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    });
    expect(cardPress).not.toHaveBeenCalled();
    expect(childPress).not.toHaveBeenCalled();
    await click(container.querySelector("section")!);
    expect(cardPress).toHaveBeenCalledTimes(1);
  });

  it("ignores a related portal action while keeping subsequent title presses active", async () => {
    const cardPress = vi.fn();
    const childPress = vi.fn();

    await act(async () =>
      root.render(
        <CandidateCard isPressable as="section" onPress={cardPress}>
          <span>Card title</span>
          {createReactPortal(
            <HeroButton
              onPress={(event) => {
                childPress();
                event.continuePropagation();
              }}
            >
              Portal action
            </HeroButton>,
            document.body,
          )}
        </CandidateCard>,
      ),
    );
    await click(button("Portal action", document.body));
    expect(childPress).toHaveBeenCalledTimes(1);
    expect(cardPress).not.toHaveBeenCalled();
    await click(container.querySelector("section > span")!);
    expect(cardPress).toHaveBeenCalledTimes(1);
  });

  it("isolates pointer events from nested button icons in PointerEvent browsers", async () => {
    class TestPointerEvent extends MouseEvent {
      pointerId = 1;
      pointerType = "mouse";
      isPrimary = true;
      width = 1;
      height = 1;
    }

    vi.stubGlobal("PointerEvent", TestPointerEvent);
    searchCandidates.mockResolvedValueOnce(response([candidate({ activeTaskId: 99 })]));
    await renderOverview();
    const action = button("acquisition.overview.viewTask");

    await pointerPress(action.querySelector("svg")! as unknown as HTMLElement);
    expect(onViewTasks).toHaveBeenCalledTimes(1);
    expect(createPortal).not.toHaveBeenCalled();
  });

  it("handles pointer presses on card text, summary and nested actions independently", async () => {
    await renderOverview();
    const card = container.querySelector<HTMLElement>('section[role="button"]')!;

    await pointerPress(card.querySelector<HTMLElement>('[title="Missing work"]')!);
    expect(createPortal).toHaveBeenCalledTimes(1);
    createPortal.mockClear();
    await pointerPress(card.querySelector("summary")!);
    expect(card.querySelector("details")!.open).toBe(true);
    await pointerPress(button("acquisition.recipes.open"));
    expect(onOpenRecipe).toHaveBeenCalledExactlyOnceWith(10);
    expect(createPortal).not.toHaveBeenCalled();
    await pointerPress(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledTimes(1);
    expect(createPortal).not.toHaveBeenCalled();
  });

  it("lets the real HeroUI workflow select open its portal and change recipes without opening details", async () => {
    controls.realSelect = true;
    await renderOverview();
    const card = container.querySelector<HTMLElement>('section[role="button"]')!;
    const trigger = button("acquisition.overview.selectRecipe", card);

    await click(trigger);
    const listbox = document.querySelector('[role="listbox"]')!;

    expect(listbox).toBeInTheDocument();
    expect(card.contains(listbox)).toBe(false);
    const option = Array.from(listbox.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (element) => element.textContent?.includes("My download recipe"),
    )!;

    await click(option);
    expect(trigger).toHaveTextContent("My download recipe");
    expect(createPortal).not.toHaveBeenCalled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ recipeDefinitionId: 20 }),
    );
    expect(createPortal).not.toHaveBeenCalled();
  });

  it("opens the resource once from the card surface and ordinary title text", async () => {
    await renderOverview();
    const card = container.querySelector<HTMLElement>('section[role="button"]')!;
    const title = card.querySelector<HTMLElement>('[title="Missing work"]')!;

    expect(card.tagName).toBe("SECTION");
    expect(card).toHaveAttribute("tabindex", "0");
    expect(card).toHaveAttribute("aria-label", "Missing work");
    expect(card.className).toContain("focus-visible:ring-2");
    expect(title.closest("button")).toBeNull();
    expect(card.querySelector("button button")).toBeNull();
    await click(card);
    expect(createPortal).toHaveBeenCalledTimes(1);
    expect(createPortal).toHaveBeenLastCalledWith(ResourceDetailModal, {
      id: 1,
      onDestroyed: expect.any(Function),
    });
    createPortal.mockClear();
    await click(title);
    expect(createPortal).toHaveBeenCalledTimes(1);
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it.each(["Enter", " "])(
    "opens the focused card once with %j, ignoring repeated keydown",
    async (key) => {
      await renderOverview();
      const card = container.querySelector<HTMLElement>('section[role="button"]')!;

      await act(async () => {
        card.focus();
        card.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }));
        card.dispatchEvent(
          new KeyboardEvent("keydown", { key, repeat: true, bubbles: true, cancelable: true }),
        );
        card.dispatchEvent(new KeyboardEvent("keyup", { key, bubbles: true, cancelable: true }));
      });
      expect(createPortal).toHaveBeenCalledTimes(1);
      expect(createPortal.mock.calls[0][1].id).toBe(1);
      expect(createAcquisition).not.toHaveBeenCalled();
    },
  );

  it.each(["Enter", " "])(
    "keeps native select and summary keyboard actions out of the card press for %j",
    async (key) => {
      await renderOverview();
      const card = container.querySelector<HTMLElement>('section[role="button"]')!;
      const summary = card.querySelector("summary")!;
      const selector = card.querySelector("select")!;

      for (const control of [selector, summary]) {
        const down = new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true });

        await act(async () => {
          control.focus();
          control.dispatchEvent(down);
          control.dispatchEvent(
            new KeyboardEvent("keyup", { key, bubbles: true, cancelable: true }),
          );
        });
        expect(down.defaultPrevented).toBe(false);
        expect(createPortal).not.toHaveBeenCalled();
      }
      // JSDOM does not synthesize the browser's summary click from a keyboard event.
      await click(summary);
      expect(card.querySelector("details")!.open).toBe(true);
      expect(createPortal).not.toHaveBeenCalled();
    },
  );

  it.each(["Enter", " "])(
    "activates a nested HeroUI task button exactly once with %j",
    async (key) => {
      searchCandidates.mockResolvedValueOnce(response([candidate({ activeTaskId: 99 })]));
      await renderOverview();
      const action = button("acquisition.overview.viewTask");

      await act(async () => {
        action.focus();
        action.dispatchEvent(
          new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }),
        );
        action.dispatchEvent(new KeyboardEvent("keyup", { key, bubbles: true, cancelable: true }));
      });
      expect(onViewTasks).toHaveBeenCalledTimes(1);
      expect(createPortal).not.toHaveBeenCalled();
    },
  );

  it("shows an unverified source and its method without promising it is downloadable", async () => {
    await renderOverview();

    expect(container).toHaveTextContent("Missing work");
    expect(container).toHaveTextContent("example.com");
    expect(container.querySelector("[data-chip]")?.textContent).toBe(
      "acquisition.overview.unverified",
    );
    expect(container.querySelector("[data-chip]")).toHaveAttribute("data-color", "default");
    expect(container).not.toHaveTextContent("acquisition.overview.unverifiedDescription");
    expect(container).toHaveTextContent("workflow.description.empty");
    expect(container).not.toHaveTextContent("acquisition.overview.method.directDownload");
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

  it("shows configuration diagnostics without mislabeling an applicable workflow as unsupported", async () => {
    searchCandidates.mockResolvedValueOnce(
      response([candidate()], {
        recipes: recipes.map((recipe) => ({
          ...recipe,
          description: "Author-defined workflow purpose",
          validation: {
            isValid: recipe.definitionId !== 10,
            diagnostics:
              recipe.definitionId === 10
                ? [
                    {
                      nodeIndex: 0,
                      severity: "error",
                      code: "missingSetting",
                      message: "Required setting is missing",
                    },
                  ]
                : [],
          },
        })),
      }),
    );
    await renderOverview();

    expect(container).toHaveTextContent("Author-defined workflow purpose");
    expect(container).toHaveTextContent("Required setting is missing");
    const diagnostic = [...container.querySelectorAll("p")].find(
      (element) => element.textContent === "Required setting is missing",
    )!;

    const details = diagnostic.closest("details")!;
    const summary = details.querySelector("summary")!;

    expect(summary).toHaveTextContent("workflow.diagnostics.needsAttention");
    expect(summary).toBeVisible();
    expect(details).not.toHaveAttribute("open");
    expect(diagnostic).not.toBeVisible();
    await click(summary);
    expect(details).toHaveAttribute("open");
    expect(diagnostic).toBeVisible();
    expect(container.querySelector("[data-chip]")).toHaveTextContent(
      "acquisition.overview.unverified",
    );
    expect(container).not.toHaveTextContent("acquisition.overview.unsupported");
    expect(button("acquisition.overview.start")).toBeDisabled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).not.toHaveBeenCalled();
    await select(
      container.querySelector<HTMLSelectElement>(
        'select[aria-label="acquisition.overview.selectRecipe"]',
      )!,
      "20",
    );
    expect(button("acquisition.overview.start")).toBeEnabled();
  });

  it("shows an uploaded torrent filename instead of its internal managed reference", async () => {
    const value = `bakabase-torrent:${"a".repeat(64)}`;

    searchCandidates.mockResolvedValueOnce(
      response([
        candidate({
          leads: [
            lead({
              kind: AcquisitionLeadKind.Torrent,
              value,
              note: "Travel photos.torrent",
              sourceName: value,
            }),
          ],
        }),
      ]),
    );
    await renderOverview();

    expect(container).toHaveTextContent("Travel photos.torrent");
    expect(container).not.toHaveTextContent("bakabase-torrent:");
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledWith(expect.objectContaining({ leadValue: value }));
  });

  it("opens the source picker directly for a resource without sources and refreshes after adding", async () => {
    searchCandidates.mockResolvedValueOnce(response([candidate({ leads: [] })]));

    await renderOverview();

    expect(container).toHaveTextContent("acquisition.overview.noSources");
    expect(
      Array.from(container.querySelectorAll("button")).some(
        (element) => element.textContent === "acquisition.overview.start",
      ),
    ).toBe(false);
    await click(button("acquisition.overview.addSource"));

    expect(createPortal).toHaveBeenCalledExactlyOnceWith(AddSourceModal, {
      resourceId: 1,
      onAdded: expect.any(Function),
    });
    expect(createPortal).not.toHaveBeenCalledWith(ResourceDetailModal, expect.anything());
    expect(createAcquisition).not.toHaveBeenCalled();
    await act(async () => createPortal.mock.calls[0][1].onAdded());
    expect(searchCandidates).toHaveBeenCalledTimes(2);
  });

  it("keeps the selected workflow when source details are expanded or collapsed", async () => {
    await renderOverview();
    const details = container.querySelector("details")!;
    const summary = details.querySelector("summary")!;
    const selector = container.querySelector<HTMLSelectElement>(
      'select[aria-label="acquisition.overview.selectRecipe"]',
    )!;

    expect(details.open).toBe(false);
    await select(selector, "20");
    await click(summary);
    expect(details.open).toBe(true);
    await click(button("acquisition.recipes.open", details));
    expect(onOpenRecipe).toHaveBeenCalledExactlyOnceWith(20);
    expect(createPortal).not.toHaveBeenCalled();
    await click(summary);
    expect(details.open).toBe(false);
    expect(selector).toHaveValue("20");
    expect(createAcquisition).not.toHaveBeenCalled();
    await click(button("acquisition.overview.start"));
    expect(createAcquisition).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ resourceId: 1, acquisitionLeadId: 11, recipeDefinitionId: 20 }),
    );
    expect(createPortal).not.toHaveBeenCalled();
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
      expect(container).toHaveTextContent("workflow.description.empty");
      expect(container).not.toHaveTextContent("acquisition.overview.method.selectedRecipe");
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
      expect(createPortal).not.toHaveBeenCalled();
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
