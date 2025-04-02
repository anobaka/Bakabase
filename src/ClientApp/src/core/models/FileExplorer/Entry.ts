import { immerable } from 'immer';
import type { IwFsEntryTaskOperationOnComplete, IwFsEntryTaskType, ResourceExistence } from '@/sdk/constants';
import { IwFsType } from '@/sdk/constants';
import { uuidv4 } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type RootEntry from '@/core/models/FileExplorer/RootEntry';
import BusinessConstants from '@/components/BusinessConstants';
import type { BTask } from '@/core/models/BTask';

const DefaultMaxChildrenHeight = 600;
const MainLineHeight = 35;
const ChildrenIndent = 15;

export interface IEntryFilter {
  keyword?: string;
  types?: IwFsType[];
  custom?: (e: Entry) => boolean;
}

export enum EntryError {
  InitializationFailed = 1,
}

export enum EntryProperty {
  ChildrenCount = 1,
  // TaskInfo = 2,
  Size = 3,
}

export enum EntryStatus {
  Default = 0,
  Loading = 1,
  Error = 2,
}

export enum EntryAsyncOperationStatus {
  NotStarted = 0,
  Running = 1,
  Completed = 2,
}

export class IwFsEntryTask {
  path: string;
  type: IwFsEntryTaskType;
  percentage: number;
  error: string;
  operationOnComplete?: IwFsEntryTaskOperationOnComplete;
  backgroundTaskId: string;
  name: string;
}

export class ResourceExistenceResult {
  existence: ResourceExistence;
  resources: string[] = [];

  constructor(init?: Partial<ResourceExistenceResult>) {
    Object.assign(this, init);
  }
}

export enum IwFsEntryAction {
  Decompress = 1,
  Play = 2,
}

export class Entry {
  id: string;
  [immerable] = true;

  root: RootEntry;

  properties: EntryProperty[];

  get isRoot(): boolean {
    return !this.parent;
  }

  children?: Entry[] = undefined;
  parent?: Entry = undefined;

  private _tmpFilter: IEntryFilter = {};
  private _filteredChildren: Entry[] = [];

  expireFilteredChildren(): void {
    this._tmpFilter = {};
    this._filteredChildren = this.children?.slice() || [];
  }

  /**
   * Performance optimization
   */
  get filteredChildren(): Entry[] {
    // console.log('Comparing filter', this._tmpFilter == this.root.filter);
    if (this._tmpFilter != this.root.filter) {
      this._tmpFilter = this.root.filter;
      this.refreshFilteredChildren();
    }
    return this._filteredChildren;
  }

  async dispose() {

  }

  private refreshFilteredChildren() {
    const { keyword, types, custom } = this._tmpFilter;
    const lowerCasedKeyword = keyword?.toLowerCase();
    this._filteredChildren = (this.children ?? []).filter(c => {
      if (!(lowerCasedKeyword == undefined || c.name.toLowerCase().includes(lowerCasedKeyword))) {
        return false;
      }
      if (types && !types.includes(c.type) && c.type != IwFsType.Directory) {
        return false;
      }
      if (custom != undefined) {
        const r = custom(c);
        if (!r) {
          return false;
        }
      }
      return true;
    });
  }

  get childrenCount(): number | undefined {
    return this.children?.length ?? this._childrenCount;
  }

  set childrenCount(value: number | undefined) {
    if (this.children) {
      return;
    }
    this._childrenCount = value;
  }

  _childrenCount?: number;

  async addChildByPath(path: string, render: boolean) {
    if (this.children) {
      const childData = (await BApi.file.getIwFsEntry({ path })).data;
      // @ts-ignore
      const child = new Entry({
        ...childData,
        parent: this,
      });
      this.children ??= [];
      this.children.push(child);
      this.children = this.children.slice()
        .sort((a, b) => a.name.localeCompare(b.name));
      this.refreshFilteredChildren();
    } else {
      if (this.childrenCount != undefined) {
        this.childrenCount += 1;
      }
    }
    if (render) {
      this.renderChildren();
    }
  }

  clearChildren(): void {
    if (this.children) {
      for (const c of this.children) {
        delete this.root.nodeMap[c.path];
      }
      this.children = undefined;
      this._filteredChildren = [];
      this.renderChildren();
    }
  }

  async replaceChildByPath(prevPath: string, newPath: string, render: boolean) {
    this.root.nodeMap[prevPath]?.delete(false);
    await this.addChildByPath(newPath, render);
  }

  get status(): EntryStatus {
    if (this.initializationStatus == EntryAsyncOperationStatus.Running) {
      return EntryStatus.Loading;
    }
    const eKeys = Object.keys(this.errors);
    if (eKeys.length > 0) {
      return EntryStatus.Error;
    }
    return EntryStatus.Default;
  }

  get actions(): IwFsEntryAction[] {
    const actions: IwFsEntryAction[] = [];
    if (!this.isDirectoryOrDrive && this.type != IwFsType.CompressedFilePart) {
      actions.push(IwFsEntryAction.Decompress);
    }
    if (this.childrenCount != undefined && this.childrenCount > 0) {
      actions.push(IwFsEntryAction.Play);
    }
    return actions;
  }

  get visible(): boolean {
    return this._ref != undefined;
  }

  initializationStatus: EntryAsyncOperationStatus = EntryAsyncOperationStatus.NotStarted;

  async initialize() {
    if (this.initializationStatus != EntryAsyncOperationStatus.NotStarted) {
      return;
    }

    if (this.type == IwFsType.Drive) {
      this.initializationStatus = EntryAsyncOperationStatus.Completed;
      return;
    }

    this.ref?.setLoading(true);
    this.initializationStatus = EntryAsyncOperationStatus.Running;
    try {
      for (const property of this.properties) {
        switch (property) {
          case EntryProperty.ChildrenCount: {
            if (this.isDirectoryOrDrive) {
              // @ts-ignore
              const info = await BApi.file.getIwFsInfo({ path: this.path }, { ignoreError: () => true });
              if (info.code) {
                this.errors[EntryError.InitializationFailed] = info.message!;
              } else {
                // console.log(info.data, this);
                Object.assign(this, info.data);
              }
            }
            break;
          }
          // case EntryProperty.TaskInfo: {
          //   // @ts-ignore
          //   const taskInfo = await BApi.file.getEntryTaskInfo({ path: this.path }, { ignoreError: () => true });
          //   if (taskInfo.code) {
          //     this.errors[EntryError.InitializationFailed] = taskInfo.message!;
          //   } else {
          //     // @ts-ignore
          //     this.task = taskInfo.data;
          //   }
          //   break;
          // }
        }
      }
    } catch (e) {
      this.errors[EntryError.InitializationFailed] = e.message;
    } finally {
      this.initializationStatus = EntryAsyncOperationStatus.Completed;
      this.ref?.setLoading(false);
    }
  }

  delete(render: boolean) {
    const {
      parent,
      root,
    } = this;
    console.log(`[${this.path}] Deleting`);
    delete root.nodeMap[this.path];
    if (parent) {
      if (parent.children) {
        parent.children = parent.children!.filter(c => c != this);
        parent.refreshFilteredChildren();
      } else {
        if (parent.childrenCount != undefined) {
          parent.childrenCount -= 1;
        }
      }
      if (render) {
        parent.renderChildren();
      }
    }
  }

  expanded: boolean = false;
  selected = false;

  passwordsForDecompressing: string[] = [];

  renaming = false;
  newName?: string = undefined;

  type: IwFsType = IwFsType.Unknown;
  path: string;

  get pathSegments(): string[] {
    return this.path.split(BusinessConstants.pathSeparator);
  }

  name: string;
  meaningfulName?: string = undefined;
  size?: number = undefined;
  ext?: string = undefined;
  creationTime?: Date = undefined;
  lastWriteTime?: Date = undefined;


  treeCache: { [path: string]: Entry } = {};

  // directoryCount?: number = undefined;
  // fileCount?: number = undefined;

  // duration?: number = undefined;

  task?: BTask;

  _ref?: IEntryRef;

  errors: { [type in EntryError]?: string } = {};

  /**
   * @deprecated The method should not be used
   */
  get isDecompressionCandidateInMultiSelection(): boolean {
    return !this.isDirectoryOrDrive;
  }

  get expandable(): boolean {
    return this.type == IwFsType.Drive || (this.type == IwFsType.Directory && (this.childrenCount == undefined || this.childrenCount > 0));
  }

  get isMedia(): boolean {
    return this.type == IwFsType.Audio || this.type == IwFsType.Video;
  }

  get isDirectory(): boolean {
    return this.type == IwFsType.Directory;
  }

  get isDirectoryOrDrive(): boolean {
    return this.type == IwFsType.Directory || this.type == IwFsType.Drive;
  }
  get isDrive(): boolean {
    return this.type == IwFsType.Drive;
  }

  /**
   * @deprecated The method should not be used
   */
  get fileSystemInfoIsLoaded(): boolean {
    return this.childrenCount != undefined;
  }

  get ref(): IEntryRef | undefined {
    return this._ref;
  }

  set ref(ref) {
    this._ref = ref;
  }

  constructor(init: Partial<Entry>) {
    Object.assign(this, init);
    this.id = uuidv4();
    // console.log(this.path, this.parent);
    if (this.parent) {
      this.root = this.parent.root;
      this.childrenWidth = this.parent.childrenWidth - ChildrenIndent;
      this.root.nodeMap[this.path] = this;
    }

    if (!this.properties) {
      this.properties = Object.keys(EntryProperty).filter((item) => !isNaN(Number(item))).map(n => parseInt(n, 10));
    }
  }

  renderChildren(): void {
    this._ref?.renderChildren();
    this.parent?.renderChildren();
  }

  /**
   * @return rendered
   */
  recalculateChildrenWidth(): boolean {
    const dom = this.ref?.dom;
    if (dom) {
      const childrenWidth = dom.parentElement!.clientWidth - (this.isRoot ? 0 : ChildrenIndent);
      if (childrenWidth != this.childrenWidth) {
        this.childrenWidth = childrenWidth;
        this.forceUpdate();
        if (this.children) {
          for (const c of this.children) {
            c.recalculateChildrenWidth();
          }
        }
        return true;
      }
    }
    return false;
  }

  select(select): void {
    this.simpleRefCall('select', 'selected', select);
  }

  highlight(highlight): void {
    this.simpleRefCall('highlight', 'highlighted', highlight);
  }

  expand(refresh?: boolean): void {
    if (this._ref) {
      this._ref.expand(refresh);
    }
  }

  collapse(): void {
    if (this._ref) {
      this._ref.collapse();
    }
  }

  get dom(): HTMLElement | null | undefined {
    return this._ref?.dom;
  }

  childrenWidth: number;

  get childrenHeight(): number {
    const { ref } = this;
    if (!ref) {
      return 0;
    }

    if (this.isRoot) {
      const parentHeight = ref.dom?.parentElement?.clientHeight ?? 0;
      if (parentHeight == 0) {
        // throw new Error('Can\'t get root children height if root dom is not initialized');
      }
      return parentHeight;
    }

    let height = 0;
    if (this.filteredChildren?.length > 0 && this.expanded) {
      for (const te of this.filteredChildren) {
        height += te.totalHeight;
      }
    }

    const maxChildrenHeight = this.isRoot ? (ref.dom?.parentElement?.clientHeight ?? 0) : DefaultMaxChildrenHeight;
    return Math.min(maxChildrenHeight, height);
  }

  get totalHeight(): number {
    return MainLineHeight + this.childrenHeight;
  }

  forceUpdate(): void {
    this._ref?.forceUpdate();
  }

  private simpleRefCall(method: string, fallbackProperty: string, value: any): void {
    console.log(this._ref, method, this);
    if (this._ref) {
      this._ref[method](value);
    } else {
      this[fallbackProperty] = value;
    }
  }
}

export interface IEntryRef {
  select: (select) => void;

  get dom(): HTMLElement | undefined | null;

  expand: (refresh?: boolean) => void;
  collapse: () => void;

  forceUpdate: () => void;
  renderChildren: () => void;

  scrollTo: (path: string) => void;

  get filteredChildren(): Entry[];

  setLoading: (loading: boolean) => void;
}
