import { beforeEach, expect, it, vi } from "vitest";
import RootEntry from "@/core/models/FileExplorer/RootEntry";

const mocks = vi.hoisted(() => ({
  watch: vi.fn(),
  stop: vi.fn(),
  keepAlive: vi.fn(),
  clear: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      startWatchingChangesInFileProcessorWorkspace: mocks.watch,
      stopWatchingChangesInFileProcessorWorkspace: mocks.stop,
      keepAliveFileProcessorWatcher: mocks.keepAlive,
    },
  },
}));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  getStandardParentPath: () => "",
}));
vi.mock("@/core/models/FileExplorer/Entry", () => ({
  Entry: class {
    path?: string;
    constructor({ path }: { path?: string }) {
      this.path = path;
    }
  },
}));
vi.mock("@/stores/iwFsEntryChangeEvents", () => ({
  useIwFsEntryChangeEventsStore: { getState: () => ({ clear: mocks.clear }) },
}));
vi.mock("@/stores/bTasks", () => ({ useBTasksStore: { getState: () => ({ tasks: [] }) } }));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.watch.mockResolvedValue({});
  mocks.stop.mockResolvedValue({});
});

it("does not start, stop, renew or clear another explorer's watcher from an independent preview", async () => {
  const previews = [new RootEntry("/first", false, false), new RootEntry("/second", false, false)];
  for (const preview of previews) await preview.initialize();
  for (const preview of previews) await preview.dispose();
  expect(mocks.watch).not.toHaveBeenCalled();
  expect(mocks.stop).not.toHaveBeenCalled();
  expect(mocks.keepAlive).not.toHaveBeenCalled();
  expect(mocks.clear).not.toHaveBeenCalled();
});

it("preserves watcher behavior by default for the main file processor", async () => {
  const root = new RootEntry("/files");
  await root.initialize();
  await root.dispose();
  expect(mocks.watch).toHaveBeenCalledWith({ path: "/files" }, expect.anything());
  expect(mocks.stop).toHaveBeenCalledTimes(2);
  expect(mocks.clear).toHaveBeenCalledTimes(2);
});
