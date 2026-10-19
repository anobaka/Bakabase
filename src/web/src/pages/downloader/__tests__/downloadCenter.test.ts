import { describe, expect, it } from "vitest";
import {
  actionTargetIds,
  btDownloadRedirect,
  buildCenterRows,
  readCenterFilter,
  type CenterTask,
} from "../downloadCenter";

const task = (id: string, overrides: Partial<CenterTask> = {}): CenterTask => ({
  id,
  provider: id.split(":")[0],
  category: id.startsWith("site:") ? "site" : "general",
  providerTaskId: id,
  name: id,
  createdAt: "2026-10-19",
  updatedAt: "2026-10-19",
  originKind: "manual",
  files: [],
  status: "running",
  outputDirectory: "/downloads",
  availableActions: ["stop", "delete"],
  ...overrides,
});
const tasks = [
  task("site:1", { siteTaskId: 1, thirdPartyId: 2 }),
  task("bt:child", { parentId: "site:1", siteTaskId: 1 }),
  task("bt:manual"),
  task("http:file"),
];

describe("unified download views", () => {
  it("counts root tasks once and expands the actual child record inline", () => {
    const collapsed = buildCenterRows(tasks, { category: "all" }, new Set());
    expect(collapsed.roots).toHaveLength(3);
    expect(collapsed.total).toBe(3);
    expect(collapsed.rows.map((row) => row.task.id)).toEqual(["site:1", "bt:manual", "http:file"]);
    const expanded = buildCenterRows(tasks, { category: "all" }, new Set(["site:1"]));
    expect(expanded.total).toBe(3);
    expect(expanded.rows.map((row) => [row.task.id, row.nested])).toEqual([
      ["site:1", false],
      ["bt:child", true],
      ["bt:manual", false],
      ["http:file", false],
    ]);
    expect(expanded.rows[1].task).toBe(tasks[1]);
  });
  it("shows linked and independent transfers using the same IDs in general and protocol views", () => {
    const filtered = buildCenterRows(
      tasks,
      { category: "general", provider: "bt" },
      new Set(["site:1"]),
    );
    expect(filtered.rows.map((row) => row.task.id)).toEqual(["bt:child", "bt:manual"]);
    expect(filtered.rows.every((row) => !row.nested)).toBe(true);
  });
  it("finds orphaned transfers and rejects duplicate list records", () => {
    const orphan = task("bt:orphan", { parentId: "site:missing" });
    const filtered = buildCenterRows(
      [...tasks, tasks[1], orphan],
      { category: "all" },
      new Set(["site:1"]),
    );
    expect(filtered.rows.filter((row) => row.task.id === "bt:child")).toHaveLength(1);
    expect(filtered.roots).toContain(orphan);
  });
  it("filters by source URL without parsing provider options", () => {
    const filtered = buildCenterRows(
      tasks,
      { category: "site", keyword: "gallery/123" },
      new Set(),
      new Map([[1, "https://site/gallery/123"]]),
    );
    expect(filtered.filtered.map((task) => task.id)).toEqual(["site:1"]);
  });
  it("preserves workflow wait and processing failures as distinct states", () => {
    const filtered = buildCenterRows(
      [task("site:1", { status: "processingFailed" }), task("site:2", { status: "waiting" })],
      { category: "all", status: "waiting" },
      new Set(),
    );
    expect(filtered.filtered.map((task) => task.id)).toEqual(["site:2"]);
  });
  it("does not schedule selected children twice when their parent owns the operation", () => {
    expect(
      actionTargetIds(tasks, ["site:1", "bt:child", "bt:manual", "bt:manual", "missing"], "stop"),
    ).toEqual(["site:1", "bt:manual"]);
    expect(actionTargetIds(tasks, ["bt:child"], "stop")).toEqual(["bt:child"]);
    expect(actionTargetIds(tasks, ["site:1", "bt:child"], "retry")).toEqual([]);
  });
  it("explicitly starts or retries a selected child even when its parent is selected", () => {
    const owned = [
      task("site:1", { availableActions: ["start", "retry"] }),
      task("bt:child", { parentId: "site:1", availableActions: ["start", "retry"] }),
    ];
    expect(actionTargetIds(owned, ["site:1", "bt:child"], "start")).toEqual(["site:1", "bt:child"]);
    expect(actionTargetIds(owned, ["site:1", "bt:child"], "retry")).toEqual(["site:1", "bt:child"]);
  });
  it("retains child commands if their parent cannot perform that action", () => {
    expect(
      actionTargetIds(
        [task("site:1", { availableActions: [] }), tasks[1]],
        ["site:1", "bt:child"],
        "stop",
      ),
    ).toEqual(["bt:child"]);
  });
  it("preserves old BT bookmarks and converts identity only once", () => {
    expect(btDownloadRedirect("?id=abc")).toBe(
      "/downloader?id=bt%3Aabc&category=general&provider=bt",
    );
    expect(btDownloadRedirect("?id=bt%3Aabc")).toBe(
      "/downloader?id=bt%3Aabc&category=general&provider=bt",
    );
    expect(readCenterFilter(new URLSearchParams("category=unknown"))).toEqual({
      category: "all",
      provider: undefined,
      id: undefined,
    });
  });
  it("handles more than 500 site tasks in one snapshot", () => {
    const snapshot = Array.from({ length: 698 }, (_, index) => task(`site:${index}`));
    expect(buildCenterRows(snapshot, { category: "all" }, new Set()).rows).toHaveLength(698);
  });
});
