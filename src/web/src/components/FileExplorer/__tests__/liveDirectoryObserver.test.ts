import { afterEach, beforeEach, expect, it, vi } from "vitest";
import RootEntry from "@/core/models/FileExplorer/RootEntry";
import { Entry } from "@/core/models/FileExplorer/Entry";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskResourceType, BTaskStatus, BTaskType, IwFsType } from "@/sdk/constants";
import type { BTask } from "@/core/models/BTask";

const mocks = vi.hoisted(() => ({ children: vi.fn(), watch: vi.fn(), stop: vi.fn(), uuid: 0 }));
vi.mock("@/sdk/BApi", () => ({ default: { file: {
  getChildrenIwFsInfo: mocks.children, startWatchingChangesInFileProcessorWorkspace: mocks.watch,
  stopWatchingChangesInFileProcessorWorkspace: mocks.stop,
} } }));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {}, uuidv4: () => String(++mocks.uuid),
  getStandardParentPath: (path: string) => path.slice(0, path.lastIndexOf("/")),
}));
const roots: RootEntry[] = [];
const ref = () => ({ dom: null, forceUpdate: vi.fn(), renderChildren: vi.fn(), select: vi.fn() }) as any;
const row = (path: string, type = IwFsType.Directory) => ({ path, name: path.split("/").pop()!, type });
const setup = async (path = "/work") => {
  const root = new RootEntry(path, false, "poll");
  root.children = []; root.ref = ref(); roots.push(root);
  await root.initialize(); return root;
};
const add = (parent: Entry, path: string, type = IwFsType.Directory) => {
  const entry = new Entry({ ...row(path, type), parent, properties: [] });
  entry.ref = ref(); parent.children!.push(entry); return entry;
};
const task = (status = BTaskStatus.Running) => ({ id: "unpack", name: "Extract archive", status,
  resourceType: BTaskResourceType.FileSystemEntry, resourceKeys: ["/work/archive.zip"],
  type: BTaskType.Decompress, createdAt: "2026-11-11T01:00:00Z", isPersistent: false, percentage: 28 } as BTask);
beforeEach(() => {
  vi.useFakeTimers(); vi.clearAllMocks();
  vi.spyOn(document, "hidden", "get").mockReturnValue(false);
  useBTasksStore.getState().setTasks([]);
  mocks.children.mockResolvedValue({ data: { entries: [] } });
});
afterEach(async () => {
  for (const root of roots.splice(0)) await root.dispose();
  useBTasksStore.getState().setTasks([]); vi.useRealTimers(); vi.restoreAllMocks();
});

it("observes task progress and errors without acquiring or clearing the singleton filesystem watcher", async () => {
  const root = await setup(); const archive = add(root, "/work/archive.zip", IwFsType.CompressedFileEntry);
  useBTasksStore.getState().setTasks([task()]);
  await Promise.resolve();
  expect(archive.task?.percentage).toBe(28); expect(archive.ref!.forceUpdate).toHaveBeenCalled();
  useBTasksStore.getState().updateTask({ ...task(BTaskStatus.Error), error: "Wrong password", briefError: "Wrong password" });
  await Promise.resolve();
  expect(archive.task?.error).toBe("Wrong password");
  useBTasksStore.getState().updateTask(task(BTaskStatus.Completed));
  await Promise.resolve(); expect(archive.task).toBeUndefined();
  expect(mocks.watch).not.toHaveBeenCalled(); expect(mocks.stop).not.toHaveBeenCalled();
});

it("reconciles created, moved and removed files while preserving unchanged node identity, selection and expanded subtrees", async () => {
  const root = await setup(); const nested = add(root, "/work/sub");
  nested.expanded = true; nested.selected = true; nested.children = [];
  const child = add(nested, "/work/sub/old.txt", IwFsType.Unknown);
  const removed = add(root, "/work/remove"); removed.children = [];
  const removedChild = add(removed, "/work/remove/file.txt", IwFsType.Unknown);
  mocks.children.mockImplementation(({ root: path }: any) => Promise.resolve({ data: { entries:
    path === "/work" ? [row("/work/sub"), row("/work/created")] : [row("/work/sub/new.txt", IwFsType.Unknown)] } }));
  await vi.advanceTimersByTimeAsync(2000);
  expect(root.children?.map(entry => entry.path)).toEqual(["/work/sub", "/work/created"]);
  expect(root.children?.[0]).toBe(nested); expect(nested.selected).toBe(true); expect(nested.expanded).toBe(true);
  expect(nested.children?.map(entry => entry.path)).toEqual(["/work/sub/new.txt"]);
  expect(root.nodeMap[child.path]).toBeUndefined(); expect(root.nodeMap[removedChild.path]).toBeUndefined();
  const renders = vi.mocked(root.ref!.renderChildren).mock.calls.length;
  await vi.advanceTimersByTimeAsync(2000);
  expect(root.ref!.renderChildren).toHaveBeenCalledTimes(renders);
});

it("updates an empty directory without requiring its non-expandable empty row to be reopened", async () => {
  const root = await setup(); expect(root.expandable).toBe(false);
  mocks.children.mockResolvedValue({ data: { entries: [row("/work/new")] } });
  await vi.advanceTimersByTimeAsync(2000);
  expect(root.children?.[0].path).toBe("/work/new");
});

it("does not recursively load collapsed folders, and caps each pass at four visible expanded directories", async () => {
  const root = await setup();
  const children = Array.from({ length: 7 }, (_, i) => add(root, `/work/${i}`));
  children.forEach(child => { child.expanded = true; child.children = []; });
  const collapsed = add(root, "/work/collapsed"); collapsed.children = [];
  const hiddenNested = add(collapsed, "/work/collapsed/nested"); hiddenNested.children = []; hiddenNested.expanded = true;
  mocks.children.mockImplementation(({ root: path }: any) => Promise.resolve({ data: { entries:
    path === "/work" ? [...children.map(child => row(child.path)), row(collapsed.path)] : [] } }));
  await vi.advanceTimersByTimeAsync(2000); expect(mocks.children).toHaveBeenCalledTimes(4);
  await vi.advanceTimersByTimeAsync(2000); expect(mocks.children).toHaveBeenCalledTimes(8);
  expect(new Set(mocks.children.mock.calls.map(call => call[0].root)).size).toBe(8);
  expect(mocks.children.mock.calls.some(call => call[0].root.includes("collapsed"))).toBe(false);
});

it("keeps independent explorers alive when another is closed and aborts a late listing without modifying its tree", async () => {
  const first = await setup("/one"), second = await setup("/two");
  let resolve!: (value: any) => void; let signal!: AbortSignal;
  mocks.children.mockImplementation(({ root }: any, options: any) => root === "/one"
    ? new Promise(done => { resolve = done; signal = options.signal; })
    : Promise.resolve({ data: { entries: [row("/two/new")] } }));
  await vi.advanceTimersByTimeAsync(2000); await first.dispose(); expect(signal.aborted).toBe(true);
  resolve({ data: { entries: [row("/one/late")] } }); await Promise.resolve();
  expect(first.children).toEqual([]); expect(second.children?.[0].path).toBe("/two/new");
  await vi.advanceTimersByTimeAsync(2000);
  expect(mocks.children.mock.calls.filter(call => call[0].root === "/two")).toHaveLength(2);
  expect(mocks.stop).not.toHaveBeenCalled();
});

it("pauses directory reads in a background tab and refreshes on focus without discarding files on errors", async () => {
  const root = await setup(); const file = add(root, "/work/kept", IwFsType.Unknown);
  vi.spyOn(document, "hidden", "get").mockReturnValue(true);
  await vi.advanceTimersByTimeAsync(4000); expect(mocks.children).not.toHaveBeenCalled();
  vi.spyOn(document, "hidden", "get").mockReturnValue(false);
  mocks.children.mockRejectedValue(new Error("temporary read failure"));
  window.dispatchEvent(new Event("focus")); await Promise.resolve(); await Promise.resolve();
  expect(mocks.children).toHaveBeenCalledOnce(); expect(root.children).toEqual([file]);
});

it("enables and pauses independent observation without rebuilding loaded files or expanded subtrees", async () => {
  const root = new RootEntry("/work", false, false);
  root.children = []; root.ref = ref(); roots.push(root);
  await root.initialize();
  const nested = add(root, "/work/sub"); nested.expanded = true; nested.selected = true; nested.children = [];
  const old = add(nested, "/work/sub/old.txt", IwFsType.Unknown);
  mocks.children.mockImplementation(({ root: path }: any) => Promise.resolve({ data: { entries:
    path === "/work" ? [row("/work/sub")] : [row(old.path, IwFsType.Unknown), row("/work/sub/new.txt", IwFsType.Unknown)] } }));
  expect(root.updateIndependentObservation("poll")).toBe(true);
  expect(root.children?.[0]).toBe(nested);
  await vi.advanceTimersByTimeAsync(2000);
  expect(nested.children?.[0]).toBe(old);
  expect(nested.children?.[1].name).toBe("new.txt");
  expect(nested.expanded).toBe(true); expect(nested.selected).toBe(true);
  const calls = mocks.children.mock.calls.length;
  expect(root.updateIndependentObservation(false)).toBe(true);
  await vi.advanceTimersByTimeAsync(4000);
  expect(mocks.children).toHaveBeenCalledTimes(calls);
  expect(root.children?.[0]).toBe(nested);
  expect(root.updateIndependentObservation(true)).toBe(false);
  expect(mocks.watch).not.toHaveBeenCalled(); expect(mocks.stop).not.toHaveBeenCalled();
});

it("starts exactly one observer when permission changes before initialization finishes", async () => {
  const root = new RootEntry("/work", false, false);
  root.children = []; root.ref = ref(); roots.push(root);
  const initialized = root.initialize();
  root.updateIndependentObservation("poll");
  await initialized;
  await vi.advanceTimersByTimeAsync(2000);
  expect(mocks.children).toHaveBeenCalledOnce();
});
