import type RootEntry from "./RootEntry";
import { Entry } from "./Entry";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";

/** Embedded explorers must not take over the file processor's singleton server watcher.
 * Observe its existing task feed and reconcile only loaded, expanded directory listings. */
export default class DirectoryObserver {
  private stopped = false;
  private busy = false;
  private timer?: ReturnType<typeof setTimeout>;
  private controller = new AbortController();
  private unsubscribe?: () => void;
  private cursor = 0;

  constructor(private readonly root: RootEntry, private readonly refreshTasks: () => void) {}

  start() {
    this.unsubscribe = useBTasksStore.subscribe((state, previous) => {
      if (state.tasks !== previous.tasks && !document.hidden && !this.stopped) this.refreshTasks();
    });
    document.addEventListener("visibilitychange", this.wake);
    window.addEventListener("focus", this.wake);
    this.refreshTasks();
    this.schedule();
  }

  stop() {
    this.stopped = true;
    clearTimeout(this.timer);
    this.controller.abort();
    this.unsubscribe?.();
    document.removeEventListener("visibilitychange", this.wake);
    window.removeEventListener("focus", this.wake);
  }

  private schedule() {
    if (!this.stopped) this.timer = setTimeout(() => void this.refresh(), 2000);
  }

  private wake = () => { if (!document.hidden) void this.refresh(); };

  private isObserved(entry: Entry) {
    if (!entry.path || !entry.isDirectoryOrDrive || !entry.expanded || !entry.children || !entry.visible) return false;
    for (let parent = entry.parent; parent; parent = parent.parent) if (!parent.expanded) return false;
    return true;
  }

  private async refresh() {
    if (this.stopped || this.busy) return;
    clearTimeout(this.timer);
    if (document.hidden) { this.schedule(); return; }
    this.busy = true;
    try {
      const directories = Object.values(this.root.nodeMap).filter(entry => this.isObserved(entry));
      // Bound work per pass; never recursively enumerate unexpanded folders or fan out a large tree.
      const count = Math.min(directories.length, 4);
      const selected = Array.from({ length: count }, (_, index) => directories[(this.cursor + index) % directories.length]);
      this.cursor = directories.length ? (this.cursor + count) % directories.length : 0;
      await Promise.all(selected.map(entry => this.refreshDirectory(entry)));
      if (!this.stopped) this.refreshTasks();
    } finally {
      this.busy = false;
      this.schedule();
    }
  }

  private async refreshDirectory(entry: Entry) {
    const controller = new AbortController();
    const abort = () => controller.abort();
    this.controller.signal.addEventListener("abort", abort, { once: true });
    const timeout = setTimeout(abort, 15_000);
    try {
      const response = await BApi.file.getChildrenIwFsInfo(
        { root: entry.path, showHiddenFiles: this.root.showHiddenFiles },
        { signal: controller.signal, showErrorToast: false },
      );
      if (this.stopped || controller.signal.aborted || document.hidden || response.code || !Array.isArray(response.data?.entries) ||
          this.root.nodeMap[entry.path] !== entry || !this.isObserved(entry)) return;
      const previous = new Map(entry.children!.map(child => [child.path, child]));
      let changed = false;
      const children = (response.data.entries ?? []).map(data => {
        const existing = data.path ? previous.get(data.path) : undefined;
        if (!existing) {
          changed = true;
          return new Entry({ ...data, parent: entry, properties: entry.properties } as Partial<Entry>);
        }
        previous.delete(existing.path);
        // Keep instance IDs, selection and expanded subtrees. Only server-owned metadata changes.
        const patch = Object.fromEntries(Object.entries(data).filter(([key]) =>
          ["name", "meaningfulName", "type", "size", "ext", "creationTime", "lastWriteTime", "childrenCount", "passwordsForDecompressing"].includes(key)));
        const differs = Object.entries(patch).some(([key, value]) =>
          JSON.stringify(existing[key as keyof Entry]) !== JSON.stringify(value));
        if (differs) { Object.assign(existing, patch); existing.forceUpdate(); changed = true; }
        return existing;
      });
      const forget = (child: Entry) => {
        child.children?.forEach(forget);
        child.select(false);
        if (this.root.nodeMap[child.path] === child) delete this.root.nodeMap[child.path];
      };
      if (previous.size) { previous.forEach(forget); changed = true; }
      if (!changed && children.some((child, index) => entry.children![index] !== child)) changed = true;
      if (changed) {
        entry.children = children;
        entry.expireFilteredChildren();
        entry.renderChildren();
      }
    } catch {
      // A failed/aborted read is not evidence that files disappeared. Keep the last listing.
    } finally {
      clearTimeout(timeout);
      this.controller.signal.removeEventListener("abort", abort);
    }
  }
}
