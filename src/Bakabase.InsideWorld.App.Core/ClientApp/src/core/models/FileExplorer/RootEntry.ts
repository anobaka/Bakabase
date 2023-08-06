import type { IEntryFilter } from '@/core/models/FileExplorer/Entry';
import { Entry } from '@/core/models/FileExplorer/Entry';
import BusinessConstants from '@/components/BusinessConstants';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { buildLogger } from '@/components/utils';
import { IwFsEntryChangeType } from '@/sdk/constants';

const log = buildLogger('TreeEntry');

enum RenderType {
  ForceUpdate = 1,
  Children = 2,
  Full = ForceUpdate | Children,
}

class RenderingQueue {
  private _queue: { path: string; type: RenderType }[] = [];

  push(path: string, type: RenderType) {
    // console.log('pushing', path, RenderType[type]);
    const last = this._queue[this._queue.length - 1];
    if (last?.path == path) {
      last.type |= type;
      // console.log(this._queue);
      return;
    }
    this._queue.push({
      path,
      type,
    });
    // console.log(this._queue);
  }

  deQueueAll() {
    const q = this._queue;
    this._queue = [];
    return q;
  }

  get length(): number {
    return this._queue.length;
  }
}

class RootEntry extends Entry {
  public nodeMap: Record<string, Entry> = {};
  public filter: IEntryFilter = {};
  private _fsWatcher: NodeJS.Timer | undefined;
  private _processingFsEvents: boolean = false;
  private _resizeObserver: ResizeObserver | undefined;

  patchFilter(filter?: Partial<IEntryFilter>) {
    this.filter = {
      ...(this.filter || {}),
      ...(filter || {}),
    };
  }

  private _initialized: boolean = false;

  async initialize() {
    if (!this._initialized) {
      this._initialized = true;
      log('Initializing...', this);
      await this._stop();
      await BApi.file.startWatchingChangesInFileProcessorWorkspace({ path: this.path });
      store.dispatch.iwFsEntryChangeEvents.clear();

      const renderingQueue = new RenderingQueue();
      this._fsWatcher = setInterval(async () => {
        if (this._processingFsEvents) {
          return;
        }
        this._processingFsEvents = true;

        const [data, dispatchers] = store.getModel('iwFsEntryChangeEvents');
        const { events } = data;
        if (events.length > 0) {
          log('handling events', events);
          dispatchers.clear();

          // Performance optimization
          // 1. Continuous created and deleted events in same directory can be merged safely;
          // 2. We keep the last task info for each entry;
          // 3. [Warning]Merge others events may cause unstable behavior

          // ignore previous tasks
          // path - event index
          const taskCache: Record<string, number> = {};
          const redundantTaskEventIndexes: number[] = [];
          for (let i = 0; i < events.length; i++) {
            const e = events[i];
            if (e.type == IwFsEntryChangeType.TaskChanged) {
              if (e.path in taskCache) {
                redundantTaskEventIndexes.push(taskCache[e.path]);
              }
              taskCache[e.path] = i;
            }
          }
          const filteredEvents = events.filter((_, i) => !redundantTaskEventIndexes.includes(i));
          if (filteredEvents.length != events.length) {
            log(`Reduced ${events.length - filteredEvents.length} task events for same path`, filteredEvents);
          }

          for (let i = 0; i < filteredEvents.length; i++) {
            const evt = filteredEvents[i];
            const changedEntryPath = evt.type == IwFsEntryChangeType.Renamed ? evt.prevPath! : evt.path;
            const changedEntry: Entry | undefined = this.nodeMap[changedEntryPath];
            const segments = evt.path.split(BusinessConstants.pathSeparator);
            const parentPath = segments.slice(0, segments.length - 1)
              .join(BusinessConstants.pathSeparator);
            const parent: Entry | undefined = this.nodeMap[parentPath];
            log(`File system entry changed: [${IwFsEntryChangeType[evt.type]}]${evt.path}`, 'Event: ', evt, 'Entry: ', changedEntry, 'Parent: ', parent);

            if (!changedEntry) {
              if (parent) {
                switch (evt.type) {
                  case IwFsEntryChangeType.Created:
                    await parent.addChildByPath(evt.path, false);
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
                  await parent.replaceChildByPath(evt.prevPath!, evt.path, false);
                  renderingQueue.push(parent.path, RenderType.Children);
                  break;
                case IwFsEntryChangeType.Changed:
                  // Ignore
                  break;
                case IwFsEntryChangeType.Deleted:
                  await changedEntry.delete(false);
                  renderingQueue.push(parent.path, RenderType.Children);
                  break;
                case IwFsEntryChangeType.TaskChanged:
                  changedEntry.task = evt.task;
                  renderingQueue.push(changedEntry.path, RenderType.ForceUpdate);
                  break;
              }
            }
          }

          let actualRenderingTimes = 0;

          const rq = renderingQueue.deQueueAll();
          for (let i = 0; i < rq.length; i++) {
            const {
              path,
              type,
            } = rq[i];
            const entry = this.nodeMap[path];
            if (entry) {
              if (type & RenderType.Children) {
                await entry.renderChildren();
                actualRenderingTimes++;
              }
              if (type & RenderType.ForceUpdate) {
                entry.forceUpdate();
                actualRenderingTimes++;
              }
            }
          }

          if (actualRenderingTimes != filteredEvents.length) {
            log(`Reduced ${filteredEvents.length - actualRenderingTimes} rendering times totally`, rq);
          }
        }

        this._processingFsEvents = false;
      }, 500);

      this.childrenWidth = this._ref!.dom!.parentElement!.clientWidth;

      this._resizeObserver = new ResizeObserver((c) => {
        const parent = c[0];
        log('Size of parent changed', parent);
        this.recalculateChildrenWidth();
      });
      this._resizeObserver.observe(this._ref!.dom!);
    }
  }

  async _stop() {
    if (this._fsWatcher) {
      console.trace();
      clearInterval(this._fsWatcher);
      await BApi.file.stopWatchingChangesInFileProcessorWorkspace();
    }
  }

  async dispose() {
    await this._stop();
  }

  constructor(path: string) {
    super({ path });
    this.root = this;
    this.expanded = true;
    this.nodeMap[this.path] = this;
  }
}


export default RootEntry;
