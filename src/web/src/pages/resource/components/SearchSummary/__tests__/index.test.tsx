import type { ReactElement } from "react";
import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import SearchSummary from "..";

import { GroupCombinator } from "@/components/ResourceFilter/models";
import { PropertyPool, PropertyType, ResourceTag, SearchOperation } from "@/sdk/constants";

const filter = (
  name: string,
  bizValue?: string,
  overrides: Partial<SearchFilter> = {},
): SearchFilter => ({
  propertyId: 1,
  propertyPool: PropertyPool.Custom,
  operation: SearchOperation.Contains,
  bizValue,
  disabled: false,
  property: {
    id: 1,
    pool: PropertyPool.Custom,
    name,
    type: PropertyType.SingleLineText,
  } as SearchFilter["property"],
  ...overrides,
});

const group = (partial: Partial<SearchFilterGroup> = {}): SearchFilterGroup => ({
  combinator: GroupCombinator.And,
  disabled: false,
  ...partial,
});

const form = (partial: Partial<SearchForm> = {}): SearchForm => ({
  page: 1,
  pageSize: 50,
  ...partial,
});

/**
 * Static markup parsed into a detached node. The repo has no DOM testing
 * library, and the summary is a pure render — no state, no effects — so
 * server rendering is enough to inspect it.
 */
const render = (element: ReactElement): HTMLElement => {
  const host = document.createElement("div");

  host.innerHTML = renderToStaticMarkup(element);

  return host;
};

/** The two-column grid every section renders into. */
const grid = (host: HTMLElement): Element => {
  const el = host.querySelector(".grid");

  if (!el) throw new Error("summary grid not rendered");

  return el;
};

/** Label cells sit at even indices, their content cell at the index after. */
const rowsOf = (host: HTMLElement): { label: string; content: Element }[] => {
  const children = Array.from(grid(host).children);

  return children.reduce<{ label: string; content: Element }[]>((out, child, i) => {
    if (i % 2 === 0) out.push({ label: child.textContent ?? "", content: children[i + 1] });

    return out;
  }, []);
};

describe("SearchSummary", () => {
  it("says so while a tab's criteria are still being fetched", () => {
    expect(render(<SearchSummary loading form={undefined} />).textContent).toBe(
      "common.state.loading",
    );
  });

  it("says so when a tab carries no criteria", () => {
    expect(render(<SearchSummary form={form()} />).textContent).toBe("resource.tab.summary.empty");
  });

  it("renders property, operator and value as separately colored parts", () => {
    const host = render(
      <SearchSummary form={form({ group: group({ filters: [filter("Author", "kubo")] }) })} />,
    );

    expect(host.querySelector(".text-foreground")?.textContent).toBe("Author");
    expect(host.querySelector(".text-secondary")?.textContent).toBe(
      "enum.searchOperation.contains",
    );
    expect(host.querySelector(".text-primary")?.textContent).toBe("kubo");
  });

  // The bug this guards: the combinator used to sit in a gutter *inside* the
  // filters cell, pushing every filter right of the keyword value below it.
  it("puts every section's content in the same grid column, unindented", () => {
    const rows = rowsOf(
      render(
        <SearchSummary
          form={form({
            keyword: "bleach",
            group: group({ filters: [filter("Author", "kubo"), filter("Series", "bleach")] }),
            tags: [ResourceTag.Pinned],
            orders: [{ property: 1, asc: true }] as SearchForm["orders"],
          })}
        />,
      ),
    );

    expect(rows.map((r) => r.label)).toEqual([
      "resource.tab.summary.keyword",
      "resource.tab.summary.filters",
      "enum.combinator.and",
      "resource.tab.summary.tags",
      "resource.tab.summary.order",
    ]);
    // Padding or margin on a content cell would break it out of alignment with
    // the column's other rows.
    for (const row of rows) {
      expect(row.content.className).not.toMatch(/\b[pm][lxs]-/);
    }
  });

  it("drops the value for operators that don't take one", () => {
    const host = render(
      <SearchSummary
        form={form({
          group: group({
            filters: [filter("Author", "kubo", { operation: SearchOperation.IsNull })],
          }),
        })}
      />,
    );

    expect(host.querySelector(".text-primary")).toBeNull();
  });

  it("keeps a disabled filter visible but struck through", () => {
    const host = render(
      <SearchSummary
        form={form({ group: group({ filters: [filter("Author", "kubo", { disabled: true })] }) })}
      />,
    );

    expect(host.textContent).toContain("Author");
    expect(host.querySelector(".line-through")).not.toBeNull();
  });

  it("renders a nested group behind a rule instead of a new column", () => {
    const host = render(
      <SearchSummary
        form={form({
          group: group({
            filters: [filter("Author", "kubo")],
            groups: [
              group({
                combinator: GroupCombinator.Or,
                filters: [filter("Series", "bleach"), filter("Year", "2001")],
              }),
            ],
          }),
        })}
      />,
    );
    const rows = rowsOf(host);

    expect(rows.map((r) => r.label)).toEqual([
      "resource.tab.summary.filters",
      "enum.combinator.and",
    ]);
    expect(rows[1].content.querySelector(".border-l")).not.toBeNull();
    expect(host.textContent).toContain("enum.combinator.or");
  });
});
