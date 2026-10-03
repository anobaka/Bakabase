import type { ReactNode } from "react";
import type * as ReactVirtualized from "react-virtualized";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskList, { TASK_ROW_MIN_HEIGHT } from "../components/TaskList";

import { PostParseTarget, PostParserSource } from "@/sdk/constants";

vi.mock("react-virtualized", async () => ({
  ...(await vi.importActual<typeof ReactVirtualized>("react-virtualized")),
  AutoSizer: ({ children }: { children: (size: { width: number; height: number }) => ReactNode }) =>
    children({ width: 960, height: 360 }),
}));

let container: HTMLDivElement;
let root: Root;

const task = (id: number): PostParserTask => ({
  id,
  source: PostParserSource.SoulPlus,
  link: `https://example.com/${id}`,
  title: `Post ${id}`,
  targets: [PostParseTarget.DownloadInfo],
});

const renderTask = (record: PostParserTask) => (
  <div data-height={record.content ? 320 : TASK_ROW_MIN_HEIGHT} role="cell">
    {record.title}
    {record.content}
  </div>
);

const show = async (records: PostParserTask[], search = "") =>
  act(async () =>
    root.render(<TaskList renderTask={renderTask} search={search} tasks={records} />),
  );

const scroll = async (top: number) => {
  const scroller = container.querySelector<HTMLElement>(".ReactVirtualized__Grid")!;

  await act(async () => {
    scroller.scrollTop = top;
    scroller.dispatchEvent(new Event("scroll", { bubbles: true }));
  });

  return scroller;
};

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  // Preserve real CellMeasurer behavior, supplying the row sizes jsdom cannot lay out.
  vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockImplementation(function () {
    const content = this.querySelector<HTMLElement>("[data-height]");

    return Number(content?.dataset.height ?? 0);
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("post parser virtual list", () => {
  it("renders only nearby records and reaches the last record below its fixed header", async () => {
    const records = Array.from({ length: 200 }, (_, index) => task(index + 1));

    await show(records);
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "201");
    expect(container.querySelectorAll('[role="columnheader"]')).toHaveLength(4);
    expect(container.querySelector('[role="row"] [role="row"]')).toBeNull();
    expect(container.querySelectorAll('[role="rowgroup"] [role="row"]')).toHaveLength(
      container.querySelectorAll("[data-task-id]").length,
    );
    expect(container.querySelectorAll("[data-task-id]").length).toBeLessThan(15);
    expect(container.querySelector('[data-task-id="200"]')).toBeNull();
    const scroller = await scroll(records.length * 180);

    expect(container.querySelector('[data-task-id="200"]')).toHaveTextContent("Post 200");
    expect(container.querySelector('[data-task-id="200"]')).toHaveAttribute("aria-rowindex", "201");
    expect(scroller.querySelector('[role="columnheader"]')).toBeNull();
  });

  it("remeasures changed result heights and resets scrolling when the local search changes", async () => {
    const records = Array.from({ length: 50 }, (_, index) => task(index + 1));

    await show(records);
    const first = container.querySelector<HTMLElement>('[data-task-id="1"]')!;

    expect(first.style.height).toBe(`${TASK_ROW_MIN_HEIGHT}px`);
    await show([{ ...records[0], content: "Long parsed result" }, ...records.slice(1)]);
    expect(container.querySelector<HTMLElement>('[data-task-id="1"]')!.style.height).toBe("320px");
    expect(container.querySelector<HTMLElement>('[data-task-id="2"]')!.style.top).toBe("320px");
    await scroll(50 * 180);
    await show([records[20], records[30]], "matched");
    expect(container.querySelector('[data-task-id="21"]')).toHaveTextContent("Post 21");
    expect(container.querySelector('[data-task-id="31"]')).toHaveTextContent("Post 31");
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "3");
  });
});
