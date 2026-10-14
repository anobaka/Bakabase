import type { Props } from "@/components/Property/components/PropertyValueRenderer";

import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FilterValueRenderer from "../components/Filter/FilterValueRenderer";

import { useDisabledChoiceKeys } from "@/hooks/useDisabledChoiceKeys";
import { PropertyPool, PropertyType, StandardValueType } from "@/sdk/constants";

const state = vi.hoisted(() => ({
  globalCounts: undefined as Record<string, number> | undefined,
  filteredCounts: undefined as Record<string, number> | undefined,
  globalStatus: { loading: false, error: false, stale: false },
  filteredStatus: { loading: false, error: false, stale: false },
  search: undefined as { keyword?: string; deviceIds?: string[] | null } | undefined,
  rendererProps: undefined as Props | undefined,
}));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwnerKey: () => "local" }));
vi.mock("@/components/Property/components/PropertyValueRenderer", () => ({
  default: (props: Props) => {
    state.rendererProps = props;

    return <RenderedOptions props={props} />;
  },
}));
vi.mock("@/hooks/useReferenceValueResourceCounts", () => ({
  useReferenceValueSearch: () => state.search,
  referenceValueCountsCriteria: (search?: { keyword?: string }) =>
    search ? { keyword: search.keyword } : {},
  useReferenceValueResourceCounts: (property?: unknown, search?: unknown) => ({
    counts: property ? (search ? state.filteredCounts : state.globalCounts) : undefined,
    ...(search ? state.filteredStatus : state.globalStatus),
    refresh: vi.fn(),
  }),
}));

const choiceIds = [
  "choice",
  "selected",
  "replacement",
  "unused",
  "known",
  "previous",
  "next",
  "explicit",
];

// Exercise the same generic extension points used by inline renderers and portals.
// The test renderer has no knowledge of count stores or resource-count props.
function RenderedOptions({
  props,
  showDescription = false,
}: {
  props: Props;
  showDescription?: boolean;
}) {
  const disabledKeys = useDisabledChoiceKeys(props.disabledKeysSource, props.disabledKeys);

  return (
    <>
      {choiceIds.map((id) => (
        <button key={id} data-choice={id} disabled={disabledKeys?.has(id)}>
          {id}
          <span data-extra={id}>{props.renderOptionExtra?.({ value: id, label: id })}</span>
        </button>
      ))}
      {showDescription && props.optionsDescription}
    </>
  );
}

const property: Props["property"] = {
  id: 1,
  pool: PropertyPool.Custom,
  type: PropertyType.SingleChoice,
  name: "Genre",
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleChoice",
  poolName: "Custom",
  order: 0,
};
let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;
let portalContainer: HTMLDivElement;
let portalRoot: ReturnType<typeof createRoot>;

function extra(id: string, surface: HTMLElement = container) {
  return surface.querySelector<HTMLElement>(`[data-extra="${id}"]`)!;
}

function badge(id: string, surface: HTMLElement = container) {
  return extra(id, surface).querySelector<HTMLElement>("span");
}

function option(id: string, surface: HTMLElement = container) {
  return surface.querySelector<HTMLButtonElement>(`[data-choice="${id}"]`)!;
}

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  state.globalCounts = undefined;
  state.filteredCounts = undefined;
  state.globalStatus = { loading: false, error: false, stale: false };
  state.filteredStatus = { loading: false, error: false, stale: false };
  state.search = undefined;
  state.rendererProps = undefined;
  container = document.createElement("div");
  portalContainer = document.createElement("div");
  document.body.append(container, portalContainer);
  root = createRoot(container);
  portalRoot = createRoot(portalContainer);
});

afterEach(async () => {
  await act(async () => {
    root.unmount();
    portalRoot.unmount();
  });
  container.remove();
  portalContainer.remove();
});

describe("filter value renderer", () => {
  it("uses global counts when an unset device filter restores as null", async () => {
    state.search = { deviceIds: null };
    state.globalCounts = { choice: 7 };
    state.filteredCounts = { choice: 0 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe("(7)");
  });

  it("shows no counts or status row for ordinary properties", async () => {
    await act(async () =>
      root.render(
        <FilterValueRenderer property={{ ...property, type: PropertyType.SingleLineText }} />,
      ),
    );

    expect(state.rendererProps!.renderOptionExtra).toBeUndefined();
    expect(state.rendererProps!.optionsDescription).toBeUndefined();
    expect(badge("choice")).toBeNull();
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it("keeps a valid replacement enabled when its current-filter count is zero", async () => {
    state.search = { keyword: "current filter" };
    state.globalCounts = { selected: 2, replacement: 7, unused: 0 };
    state.filteredCounts = { selected: 2, replacement: 0, unused: 0 };
    await act(async () =>
      root.render(<FilterValueRenderer dbValue="selected" property={property} />),
    );

    expect(extra("selected").textContent).toBe("(2)");
    expect(extra("replacement").textContent).toBe("");
    expect(option("replacement").disabled).toBe(false);
    expect(option("unused").disabled).toBe(true);
  });

  it("does not turn missing or unavailable counts into disabled choices", async () => {
    state.search = { keyword: "current filter" };
    state.filteredCounts = { choice: 0 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(option("choice").disabled).toBe(false);

    state.globalCounts = { known: 3 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(option("choice").disabled).toBe(false);
  });

  it("updates a mounted option extra across global and filtered searches", async () => {
    state.globalCounts = { choice: 7 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    const capturedProps = state.rendererProps!;

    await act(async () =>
      portalRoot.render(<RenderedOptions showDescription props={capturedProps} />),
    );
    expect(extra("choice", portalContainer).textContent).toBe("(7)");

    state.search = { keyword: "limited" };
    state.filteredStatus.loading = true;
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice", portalContainer).textContent).toBe("(7)");
    expect(portalContainer.querySelector('[role="status"]')!.textContent).toBe(
      "property.reference.updatingCounts",
    );

    state.filteredCounts = { choice: 0 };
    state.filteredStatus.loading = false;
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice", portalContainer).textContent).toBe("");
    expect(badge("choice", portalContainer)).toBeNull();

    state.search = undefined;
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice", portalContainer).textContent).toBe("(7)");
  });

  it("keeps global counts visible and availability unchanged during the first filtered request", async () => {
    state.globalCounts = { choice: 1_000, unused: 0 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    const previousBadge = badge("choice");

    state.search = { keyword: "new filter" };
    state.filteredStatus.loading = true;
    await act(async () => root.render(<FilterValueRenderer property={property} />));

    expect(extra("choice").textContent).toBe(`(${(1_000).toLocaleString()})`);
    expect(badge("choice")).toBe(previousBadge);
    expect(badge("choice")!.title).toBe("property.reference.updatingCounts");
    expect(container.querySelector('[role="status"]')!.textContent).toBe(
      "property.reference.updatingCounts",
    );
    expect(option("choice").disabled).toBe(false);
    expect(option("unused").disabled).toBe(true);
  });

  it("retains the last displayed global counts instead of an older filtered result", async () => {
    state.globalCounts = { choice: 1_000 };
    state.search = { keyword: "first filter" };
    state.filteredCounts = { choice: 2 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe("(2)");

    state.search = undefined;
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe(`(${(1_000).toLocaleString()})`);

    state.search = { keyword: "second filter" };
    state.filteredStatus = { loading: true, error: false, stale: true };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe(`(${(1_000).toLocaleString()})`);
    expect(badge("choice")!.title).toBe("property.reference.updatingCounts");

    state.filteredCounts = { choice: 3 };
    state.filteredStatus = { loading: false, error: false, stale: false };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe("(3)");
    expect(container.querySelector('[role="status"]')!.textContent).toBe("");
  });

  it("displays compact filtered counts without reserving space for global totals", async () => {
    state.globalCounts = { choice: 1_000, unused: 0 };
    state.search = { keyword: "filter" };
    state.filteredCounts = { choice: 9, unused: 0 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(badge("choice")!.style.width).toBe("");
    expect(badge("unused")).toBeNull();
    for (const count of [10, 1_000, 0, 9]) {
      state.filteredCounts = { choice: count, unused: 0 };
      await act(async () => root.render(<FilterValueRenderer property={property} />));
      expect(extra("choice").textContent).toBe(count > 0 ? `(${count.toLocaleString()})` : "");
      if (count > 0) {
        expect(badge("choice")!.style.width).toBe("");
        expect(badge("choice")!.style.minWidth).toBe("");
      } else {
        expect(badge("choice")).toBeNull();
      }
      expect(option("choice").disabled).toBe(false);
      expect(option("unused").disabled).toBe(true);
    }
  });

  it("replaces the displayed result set and clears stale status when fresh counts arrive", async () => {
    state.globalCounts = { previous: 5, next: 10 };
    state.search = { keyword: "first filter" };
    state.filteredCounts = { previous: 5 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));

    state.search = { keyword: "next filter" };
    state.filteredStatus = { loading: true, error: false, stale: true };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("previous").textContent).toBe("(5)");

    state.filteredCounts = { next: 10 };
    state.filteredStatus = { loading: false, error: false, stale: false };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("previous").textContent).toBe("");
    expect(extra("next").textContent).toBe("(10)");
    expect(badge("next")!.className).toContain("opacity-70");
    expect(container.querySelector('[role="status"]')!.textContent).toBe("");
  });

  it("retains displayed counts and availability when a filtered update fails", async () => {
    state.globalCounts = { choice: 10, unused: 0 };
    state.search = { keyword: "first filter" };
    state.filteredCounts = { choice: 2, unused: 0 };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    const previousBadge = badge("choice");

    state.search = { keyword: "failing filter" };
    state.filteredCounts = undefined;
    state.filteredStatus = { loading: false, error: true, stale: false };
    await act(async () => root.render(<FilterValueRenderer property={property} />));
    expect(extra("choice").textContent).toBe("(2)");
    expect(badge("choice")).toBe(previousBadge);
    expect(badge("choice")!.title).toBe("property.reference.countsUpdateFailed");
    expect(option("choice").disabled).toBe(false);
    expect(option("unused").disabled).toBe(true);
  });

  it.each([
    { field: "id", changed: { id: 2 } },
    { field: "pool", changed: { pool: PropertyPool.Reserved } },
    { field: "type", changed: { type: PropertyType.MultipleChoice } },
  ])(
    "isolates counts and captured extras when the property $field changes",
    async ({ changed }) => {
      state.globalCounts = { choice: 1_000, unused: 0 };
      await act(async () => root.render(<FilterValueRenderer property={property} />));
      const capturedProps = state.rendererProps!;

      await act(async () => portalRoot.render(<RenderedOptions props={capturedProps} />));
      const nextProperty = { ...property, ...changed };

      state.globalCounts = undefined;
      state.globalStatus.loading = true;
      await act(async () => root.render(<FilterValueRenderer property={nextProperty} />));
      expect(badge("choice")).toBeNull();
      expect(option("unused").disabled).toBe(false);
      expect(container.querySelector('[role="status"]')!.textContent).toBe(
        "property.reference.loadingCounts",
      );

      state.globalCounts = { choice: 3, unused: 4 };
      state.globalStatus.loading = false;
      await act(async () => root.render(<FilterValueRenderer property={nextProperty} />));
      expect(extra("choice").textContent).toBe("(3)");
      expect(option("unused").disabled).toBe(false);
      expect(extra("choice", portalContainer).textContent).toBe(`(${(1_000).toLocaleString()})`);
      expect(option("unused", portalContainer).disabled).toBe(true);
    },
  );

  it("updates captured option availability while retaining explicit restrictions", async () => {
    const disabledKeys = new Set(["explicit"]);

    state.globalCounts = { unused: 0 };
    await act(async () =>
      root.render(<FilterValueRenderer disabledKeys={disabledKeys} property={property} />),
    );
    const capturedProps = state.rendererProps!;

    await act(async () => portalRoot.render(<RenderedOptions props={capturedProps} />));
    expect(option("unused", portalContainer).disabled).toBe(true);
    expect(option("explicit", portalContainer).disabled).toBe(true);

    state.globalCounts = { unused: 4 };
    await act(async () =>
      root.render(<FilterValueRenderer disabledKeys={disabledKeys} property={property} />),
    );
    expect(option("unused", portalContainer).disabled).toBe(false);
    expect(option("explicit", portalContainer).disabled).toBe(true);
  });

  it("preserves caller extras and descriptions alongside filter counts", async () => {
    state.globalCounts = { choice: 3 };
    await act(async () =>
      root.render(
        <FilterValueRenderer
          optionsDescription={<p>Caller description</p>}
          property={property}
          renderOptionExtra={({ value }) => <em>{`extra:${value}`}</em>}
        />,
      ),
    );

    expect(extra("choice").textContent).toBe("extra:choice(3)");
    expect(container.querySelectorAll("p")).toHaveLength(1);
    expect(container.querySelector("p")!.textContent).toBe("Caller description");
    const capturedProps = state.rendererProps!;

    await act(async () =>
      portalRoot.render(<RenderedOptions showDescription props={capturedProps} />),
    );
    expect(extra("choice", portalContainer).textContent).toBe("extra:choice(3)");
    expect(portalContainer.querySelector("p")!.textContent).toBe("Caller description");
  });
});
