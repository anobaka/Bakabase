import type { ReactNode } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useRef, useState } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskList from "../components/TaskList";
import { PostParseTarget, PostParserSource } from "@/sdk/constants";

let container: HTMLDivElement;
let root: Root;
const task = (id: number): PostParserTask => ({
  id,
  source: PostParserSource.SoulPlus,
  link: `https://example.com/${id}`,
  title: `Post ${id}`,
  targets: [PostParseTarget.DownloadInfo],
});
const records = () => Array.from({ length: 20 }, (_, index) => task(index + 1));
const renderTask = (record: PostParserTask) => (
  <div role="cell">
    {record.title}
    {record.content}
  </div>
);
const viewport = () => container.querySelector<HTMLElement>("[data-post-parser-scroll]")!;
const show = async (
  tasks: PostParserTask[],
  options: {
    search?: string;
    pageKey?: string;
    locateRequest?: { taskId: number; sequence: number };
    onLocated?: (sequence: number) => void;
    renderTask?: (record: PostParserTask) => ReactNode;
  } = {},
) =>
  act(async () =>
    root.render(<TaskList tasks={tasks} search="" renderTask={renderTask} {...options} />),
  );

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("post parser paged scrolling", () => {
  it("keeps the table header and complete page in the same scroll flow", async () => {
    await show(records());
    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "21");
    expect(container.querySelectorAll('[role="columnheader"]')).toHaveLength(4);
    expect(viewport().querySelectorAll('[role="columnheader"]')).toHaveLength(4);
    expect(viewport().querySelectorAll('[role="rowgroup"] [role="row"]')).toHaveLength(20);
    expect(viewport().querySelector('[data-task-id="20"]')).toHaveAttribute("aria-rowindex", "21");
  });

  it("preserves the scroll container and every row across repeated polling and unrelated updates", async () => {
    const current = records();
    await show(current);
    const scroller = viewport();
    const rows = [...scroller.querySelectorAll("[data-task-id]")];
    scroller.scrollTop = 900;
    for (let i = 0; i < 15; i++) {
      await show(current.map((record) => ({ ...record })));
      expect(viewport()).toBe(scroller);
      expect(viewport().scrollTop).toBe(900);
      expect([...scroller.querySelectorAll("[data-task-id]")]).toEqual(rows);
    }
    await show(
      current.map((record) => (record.id === 20 ? { ...record, title: "Updated result" } : record)),
    );
    expect(viewport().scrollTop).toBe(900);
    expect(container.querySelector('[data-task-id="20"]')).toBe(rows[19]);
    expect(rows[19]).toHaveTextContent("Updated result");
  });

  it("retains expanded row content and focus when asynchronous content changes its height", async () => {
    function Detail({ loaded }: { loaded: boolean }) {
      const [open, setOpen] = useState(false);
      return (
        <div role="cell">
          <button onClick={() => setOpen((value) => !value)}>Details</button>
          {open && <div>{loaded ? "Loaded history and file paths" : "Loading history"}</div>}
        </div>
      );
    }
    const current = [task(1), task(2)];
    await show(current, { renderTask: () => <Detail loaded={false} /> });
    const first = container.querySelector('[data-task-id="1"]')!;
    const button = first.querySelector("button")!;
    button.focus();
    await act(async () => button.click());
    await show(
      current.map((record) => ({ ...record })),
      { renderTask: () => <Detail loaded /> },
    );
    expect(container.querySelector('[data-task-id="1"]')).toBe(first);
    expect(first).toHaveTextContent("Loaded history and file paths");
    expect(document.activeElement).toBe(button);
  });

  it("resets only for an explicit page or search change", async () => {
    const current = records();
    await show(current, { pageKey: "1:20:all" });
    viewport().scrollTop = 600;
    await show(current, { pageKey: "2:20:all" });
    expect(viewport().scrollTop).toBe(0);
    viewport().scrollTop = 400;
    await show(current, { pageKey: "2:20:all", search: "matched" });
    expect(viewport().scrollTop).toBe(0);
  });

  it("locates once per explicit request and never replays navigation on polling", async () => {
    const current = records();
    const onLocated = vi.fn();
    await show(current);
    const row = container.querySelector<HTMLElement>('[data-task-id="10"]')!;
    vi.spyOn(row, "getBoundingClientRect").mockReturnValue({ top: 1200, height: 200 } as DOMRect);
    Object.defineProperty(viewport(), "clientHeight", { value: 400 });
    const request = { taskId: 10, sequence: 1 };
    await show(current, { locateRequest: request, onLocated });
    expect(viewport().scrollTop).toBeGreaterThan(0);
    expect(row).toHaveAttribute("data-located", "true");
    expect(onLocated).toHaveBeenCalledExactlyOnceWith(1);
    viewport().scrollTop = 30;
    await show(
      current.map((record) => ({ ...record })),
      { locateRequest: request, onLocated },
    );
    expect(viewport().scrollTop).toBe(30);
    expect(onLocated).toHaveBeenCalledTimes(1);
    await show(current, { locateRequest: { ...request, sequence: 2 }, onLocated });
    expect(viewport().scrollTop).toBeGreaterThan(0);
    expect(onLocated).toHaveBeenLastCalledWith(2);
  });

  it("uses the page viewport for filters, rows and centered explicit navigation without a nested vertical scroller", async () => {
    const onLocated = vi.fn();
    function Page({
      tasks,
      pageKey,
      locateRequest,
    }: {
      tasks: PostParserTask[];
      pageKey: string;
      locateRequest?: { taskId: number; sequence: number };
    }) {
      const scrollRef = useRef<HTMLDivElement>(null);
      return (
        <div ref={scrollRef} data-post-parser-scroll className="overflow-y-auto">
          <div data-page-filters>Filters and a compact review action</div>
          <TaskList
            scrollRef={scrollRef}
            tasks={tasks}
            pageKey={pageKey}
            search=""
            renderTask={renderTask}
            locateRequest={locateRequest}
            onLocated={onLocated}
          />
        </div>
      );
    }
    await act(async () => root.render(<Page tasks={records()} pageKey="1" />));
    const scroller = viewport();
    expect(container.querySelectorAll("[data-post-parser-scroll]")).toHaveLength(1);
    expect(scroller.querySelector("[data-page-filters]")).not.toBeNull();
    expect(scroller.querySelector('[role="table"]')!.parentElement).not.toHaveClass(
      "overflow-x-auto",
    );
    expect(scroller.querySelector('[role="table"]')!.parentElement).not.toHaveClass(
      "overflow-y-auto",
    );
    expect(scroller.querySelector('[role="rowgroup"]')).not.toHaveClass("overflow-y-auto");
    const row = scroller.querySelector<HTMLElement>('[data-task-id="10"]')!;
    vi.spyOn(scroller, "getBoundingClientRect").mockReturnValue({ top: 100 } as DOMRect);
    vi.spyOn(row, "getBoundingClientRect").mockReturnValue({ top: 700, height: 160 } as DOMRect);
    Object.defineProperty(scroller, "clientHeight", { value: 480 });
    scroller.scrollTop = 80;
    const locateRequest = { taskId: 10, sequence: 1 };
    await act(async () =>
      root.render(<Page tasks={records()} pageKey="1" locateRequest={locateRequest} />),
    );
    expect(scroller.scrollTop).toBe(520);
    expect(onLocated).toHaveBeenCalledExactlyOnceWith(1);
    scroller.scrollTop = 320;
    await act(async () =>
      root.render(<Page tasks={records()} pageKey="1" locateRequest={locateRequest} />),
    );
    expect(viewport()).toBe(scroller);
    expect(scroller.scrollTop).toBe(320);
    await act(async () => root.render(<Page tasks={records()} pageKey="2" />));
    expect(viewport()).toBe(scroller);
    expect(scroller.scrollTop).toBe(0);
  });

  it("waits until the requested task arrives on the page before consuming its location request", async () => {
    const onLocated = vi.fn();
    const request = { taskId: 50, sequence: 1 };
    await show(records(), { locateRequest: request, onLocated });
    expect(onLocated).not.toHaveBeenCalled();
    await show([task(49), task(50)], { locateRequest: request, onLocated, pageKey: "3:20:all" });
    expect(container.querySelector('[data-task-id="50"]')).toHaveAttribute("data-located", "true");
    expect(onLocated).toHaveBeenCalledExactlyOnceWith(1);
  });

  it("clears the location highlight after three seconds despite polling updates", async () => {
    vi.useFakeTimers();
    const current = records();
    const locateRequest = { taskId: 2, sequence: 1 };
    await show(current, { locateRequest });
    expect(container.querySelector('[data-task-id="2"]')).toHaveAttribute("data-located", "true");
    await act(async () => {
      vi.advanceTimersByTime(1000);
    });
    await show(
      current.map((record) => ({ ...record })),
      { locateRequest },
    );
    await act(async () => {
      vi.advanceTimersByTime(2000);
    });
    expect(container.querySelector('[data-task-id="2"]')).not.toHaveAttribute("data-located");
  });
});
