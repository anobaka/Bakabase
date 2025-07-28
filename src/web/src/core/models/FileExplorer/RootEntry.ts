import type { IEntryFilter } from "@/core/models/FileExplorer/Entry";
import type { BTask } from "@/core/models/BTask";

import diff from "deep-diff";
import _ from "lodash";

import { Entry } from "@/core/models/FileExplorer/Entry";
import BusinessConstants from "@/components/BusinessConstants";
import BApi from "@/sdk/BApi";
import { buildLogger, splitPathIntoSegments } from "@/components/utils";
import {
  BTaskResourceType,
  BTaskStatus,
  IwFsEntryChangeType,
  IwFsType,
} from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { useIwFsEntryChangeEventsStore } from "@/stores/iwFsEntryChangeEvents";

const log = buildLogger("RootEntry");

enum RenderType {
  ForceUpdate = 1,
  Children = 2,
}

type RenderingQueueItem = { path: string; type: RenderType };

class RenderingQueue {
  private _queue: RenderingQueueItem[] = [];

  push(path: string, type: RenderType) {
    this._queue.push({
      path,
      type,
    });
  }

  deQueueAll(): RenderingQueueItem[] {
    const q = this._queue;

    this._queue = [];

    return q;
  }

  shrink() {
    const map: { [key: string]: RenderType } = {};

    for (const i in this._queue) {
      const item = this._queue[i]!;

      if (item.path in map) {
        map[item.path] |= item.type;
      } else {
        map[item.path] = item.type;
      }
    }
    this._queue = Object.keys(map).map<RenderingQueueItem>((x) => ({
      path: x,
      type: map[x]!,
    }));
  }

  get length(): number {
    return this._queue.length;
  }
}

class RootEntry extends Entry {
  public nodeMap: Record<string, Entry> = {};
  public filter: IEntryFilter = {};
  private _fsWatcher: ReturnType<typeof setInterval> | undefined;
  private _processingFsEvents: boolean = false;
  private _resizeObserver: ResizeObserver | undefined;

  patchFilter(filter?: Partial<IEntryFilter>) {
    this.filter = {
      ...(this.filter || {}),
      ...(filter || {}),
    };
  }

  private _initialized: boolean = false;

  async _compareBTasks(renderingQueue: RenderingQueue) {
    const self = this;
    const bTasks = useBTasksStore.getState().tasks;
    const targetTasks = bTasks.filter(
      (x) =>
        x.resourceType == BTaskResourceType.FileSystemEntry &&
        (x.status == BTaskStatus.Running ||
          x.status == BTaskStatus.Paused ||
          x.status == BTaskStatus.Error ||
          x.status == BTaskStatus.NotStarted),
    );
    const targetTasksMap: Record<string, BTask[]> = _.flatMap(
      targetTasks,
      (t) =>
        (t.resourceKeys ?? []).map((k) => ({
          path: k as string,
          task: t,
        })),
    ).reduce((s, t) => {
      if (t.path in s) {
        s[t.path].push(t.task);
      } else {
        s[t.path] = [t.task];
      }

      return s;
    }, {});

    // log(targetTasks);

    for (const path of Object.keys(self.nodeMap)) {
      const node = self.nodeMap[path]!;
      const prevTask = node.task;
      const incomingTasks = targetTasksMap[path] ?? [];
      let task = incomingTasks[0];

      if (incomingTasks.length > 1) {
        task = _.sortBy(incomingTasks, (x) => {
          switch (x.status) {
            case BTaskStatus.Error:
              return -1;
            case BTaskStatus.Running:
              return 0;
            case BTaskStatus.Paused:
              return 1;
            case BTaskStatus.NotStarted:
              return 2;
            case BTaskStatus.Completed:
            case BTaskStatus.Cancelled:
              return 999;
          }
        })[0];
      }
      const differences = diff(prevTask, task);

      if (differences) {
        log(
          "TaskChanged",
          differences,
          "current: ",
          task,
          "previous: ",
          prevTask,
        );
        renderingQueue.push(path, RenderType.ForceUpdate);
        node.task = task;
      }
    }
  }

  async _consumeFsEvents(renderingQueue: RenderingQueue) {
    const self = this;
    const data = useIwFsEntryChangeEventsStore.getState().contexts;
    const dispatchers = {
      addRange: useIwFsEntryChangeEventsStore.getState().addRange,
      clear: useIwFsEntryChangeEventsStore.getState().clear,
      // ... 其它 action如有
    };
    const { events } = data;

    if (events.length > 0) {
      log("handling events", events, self);
      dispatchers.clear();

      // Performance optimization
      // 1. Continuous created and deleted events in same directory can be merged safely;
      // 2. We keep the last task info for each entry;
      // 3. [Warning]Merge others events may cause unstable behavior

      // ignore previous tasks
      // path - event index
      const taskCache: { [key: string]: number } = {};
      const redundantTaskEventIndexes: number[] = [];

      for (let i = 0; i < events.length; i++) {
        const e = events[i]!;

        if (e.type == IwFsEntryChangeType.TaskChanged) {
          if (e.path in taskCache) {
            redundantTaskEventIndexes.push(taskCache[e.path]!);
          }
          taskCache[e.path] = i;
        }
      }
      const filteredEvents = events.filter(
        (_, i) => !redundantTaskEventIndexes.includes(i),
      );

      if (filteredEvents.length != events.length) {
        log(
          `Reduced ${events.length - filteredEvents.length} task events for same path`,
          filteredEvents,
        );
      }

      for (let i = 0; i < filteredEvents.length; i++) {
        const evt = filteredEvents[i]!;
        const changedEntryPath =
          evt.type == IwFsEntryChangeType.Renamed ? evt.prevPath! : evt.path;
        const changedEntry: Entry | undefined = self.nodeMap[changedEntryPath];
        const segments = splitPathIntoSegments(evt.path);
        const parentPath = segments
          .slice(0, segments.length - 1)
          .join(BusinessConstants.pathSeparator);
        const parent: Entry | undefined = self.nodeMap[parentPath];

        log(
          "Try to locate parent",
          "path:",
          parentPath,
          "parent:",
          parent,
          "nodeMap:",
          self.nodeMap,
        );
        log(
          `File system entry changed: [${IwFsEntryChangeType[evt.type]}]${evt.path}`,
          "Event: ",
          evt,
          "Entry: ",
          changedEntry,
        );

        if (!changedEntry) {
          if (parent) {
            switch (evt.type) {
              case IwFsEntryChangeType.Created:
                await parent.addChildByPath(evt.path, false);
                log("Add to children", evt.path, parent.children);
                renderingQueue.push(parent.path, RenderType.Children);
                break;
              case IwFsEntryChangeType.Renamed:
                // Not rendered, ignore
                break;
              case IwFsEntryChangeType.Changed:
                // Not rendered, ignore
                break;
              case IwFsEntryChangeType.Deleted:
                if (parent.childrenCount != undefined) {
                  parent.childrenCount! -= 1;
                }
                renderingQueue.push(parent.path, RenderType.ForceUpdate);
                break;
              case IwFsEntryChangeType.TaskChanged:
                // Not rendered, ignore
                break;
            }
          }
        } else {
          switch (evt.type) {
            case IwFsEntryChangeType.Created:
              // Never happens
              break;
            case IwFsEntryChangeType.Renamed:
              if (parent) {
                await parent.replaceChildByPath(evt.prevPath!, evt.path, false);
                renderingQueue.push(parent.path, RenderType.Children);
              }
              break;
            case IwFsEntryChangeType.Changed:
              // Ignore
              break;
            case IwFsEntryChangeType.Deleted:
              if (parent) {
                changedEntry.delete(false);
                renderingQueue.push(parent.path, RenderType.Children);
              }
              break;
          }
        }
      }
    }
  }

  async initialize() {
    if (!this._initialized) {
      this._initialized = true;
      log("Initializing...", this);
      await this._stop();
      // @ts-ignore
      if (this.path) {
        log("Start watching", this.path, this);
        await BApi.file.startWatchingChangesInFileProcessorWorkspace(
          { path: this.path },
          { showErrorToast: () => false },
        );
        const renderingQueue = new RenderingQueue();
        const self = this;

        this._fsWatcher = setInterval(async () => {
          if (self._processingFsEvents) {
            return;
          }
          self._processingFsEvents = true;

          await self._consumeFsEvents(renderingQueue);
          await self._compareBTasks(renderingQueue);

          const originalRenderingTimes = renderingQueue.length;

          renderingQueue.shrink();
          let actualRenderingTimes = 0;

          const rq = renderingQueue.deQueueAll();

          // log('RenderingQueue', rq);
          for (let i = 0; i < rq.length; i++) {
            const { path, type } = rq[i]!;
            const entry = self.nodeMap[path];

            if (entry) {
              if (type & RenderType.Children) {
                entry.renderChildren();
                actualRenderingTimes++;
              }
              if (type & RenderType.ForceUpdate) {
                entry.forceUpdate();
                actualRenderingTimes++;
              }
            }
          }

          if (actualRenderingTimes != originalRenderingTimes) {
            log(
              `Reduced ${originalRenderingTimes - actualRenderingTimes} rendering times totally`,
              rq,
            );
          }

          self._processingFsEvents = false;
        }, 500);
      }

      this.childrenWidth = this._ref?.dom!.parentElement?.clientWidth ?? 0;

      this._resizeObserver = new ResizeObserver((c) => {
        const parent = c[0];

        log("Size of parent changed", parent);
        this.recalculateChildrenWidth();
      });
      this._resizeObserver.observe(this._ref!.dom!);
    }
  }

  async _stop() {
    // if (this._fsWatcher) {
    // console.trace();
    log("Stopping watcher", this, this.path, this._fsWatcher);
    clearInterval(this._fsWatcher);
    await BApi.file.stopWatchingChangesInFileProcessorWorkspace();
    useIwFsEntryChangeEventsStore.getState().clear();
    // }
  }

  async dispose(): Promise<void> {
    await this._stop();
  }

  constructor(path?: string) {
    super({ path });
    this.root = this;
    this.expanded = true;
    this.type = IwFsType.Directory;
    this.nodeMap[this.path] = this;
  }
}

export default RootEntry;
