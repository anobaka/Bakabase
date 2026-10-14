import type { Resource } from "@/core/models/Resource";
import type { SearchForm, SearchFormOrderModel } from "@/pages/resource/models";
import { ResourceSearchSortableProperty } from "@/sdk/constants";
import type { ManagementSource } from "./api";
import { resourceSelectionKey } from "./resourceIdentity";

export interface ResourcePageResult {
  data: Resource[];
  totalCount: number;
}
export type SourceResourceSearch = (
  source: ManagementSource,
  form: SearchForm,
  signal: AbortSignal,
) => Promise<ResourcePageResult>;

const batchSize = 100;
const cachedPageLimit = 24;
type Stream = {
  source: ManagementSource;
  page: number;
  queue: Resource[];
  exhausted: boolean;
  total: number;
};

function date(value: string | undefined) {
  const parsed = value ? Date.parse(value) : Number.NaN;
  return Number.isNaN(parsed) ? undefined : parsed;
}

function orderedValue(resource: Resource, property: ResourceSearchSortableProperty) {
  switch (property) {
    case ResourceSearchSortableProperty.FileCreateDt:
      return resource.path ? date(resource.fileCreatedAt) : undefined;
    case ResourceSearchSortableProperty.FileModifyDt:
      return resource.path ? date(resource.fileModifiedAt) : undefined;
    case ResourceSearchSortableProperty.Filename:
      return resource.path?.split(/[\\/]/).filter(Boolean).at(-1)?.toUpperCase();
    case ResourceSearchSortableProperty.AddDt:
      return date(resource.createdAt);
    case ResourceSearchSortableProperty.PlayedAt:
      return date(resource.playedAt);
    case ResourceSearchSortableProperty.HealthScore:
      return resource.healthScore;
  }
}

export function compareOwnedResources(a: Resource, b: Resource, orders?: SearchFormOrderModel[]) {
  if (!orders?.length) {
    const pinned = Number(!!b.pinned) - Number(!!a.pinned);
    if (pinned) return pinned;
    if (a.id !== b.id) return b.id - a.id;
  }
  for (const order of orders ?? []) {
    const av = orderedValue(a, order.property);
    const bv = orderedValue(b, order.property);
    if (av == null || bv == null) {
      if (av == null && bv == null) continue;
      // Native sorting keeps absent filesystem/health values last in either direction.
      const missingLast =
        order.property !== ResourceSearchSortableProperty.PlayedAt &&
        order.property !== ResourceSearchSortableProperty.AddDt;
      const comparison = av == null ? 1 : -1;
      return missingLast ? comparison : comparison * (order.asc ? -1 : 1);
    }
    if (av !== bv) return (av < bv ? -1 : 1) * (order.asc ? 1 : -1);
  }
  return (a.owner?.serverId ?? "").localeCompare(b.owner?.serverId ?? "") || b.id - a.id;
}

/** Merge bounded sorted streams. A page never downloads a whole remote library. */
export class ManagedResourcePager {
  private streams: Stream[] = [];
  private pages = new Map<number, Resource[]>();
  private nativePages = new Map<number, ResourcePageResult>();
  private pageRequests: Promise<void> = Promise.resolve();
  private consumedPages = 0;
  private initialized = false;
  readonly failures = new Map<string, Error>();
  totalCount = 0;

  constructor(
    private readonly sources: ManagementSource[],
    private readonly form: SearchForm,
    private readonly request: SourceResourceSearch,
    private readonly signal: AbortSignal,
  ) {}

  private checkCancelled() {
    this.signal.throwIfAborted();
  }

  private async fill(stream: Stream) {
    this.checkCancelled();
    if (stream.queue.length || stream.exhausted) return;
    try {
      const response = await this.request(
        stream.source,
        {
          ...this.form,
          page: stream.page + 1,
          pageSize: batchSize,
        },
        this.signal,
      );
      this.checkCancelled();
      stream.page += 1;
      stream.total = response.totalCount;
      stream.queue = response.data.map((resource) => ({ ...resource, owner: stream.source }));
      stream.exhausted = !response.data.length || stream.page * batchSize >= response.totalCount;
    } catch (error) {
      this.checkCancelled();
      this.failures.set(
        stream.source.serverId,
        error instanceof Error ? error : new Error(String(error)),
      );
      stream.exhausted = true;
      stream.queue = [];
      stream.total = 0;
    }
  }

  private async fillPending(streams: Stream[]) {
    for (let index = 0; index < streams.length; index += 4)
      await Promise.all(streams.slice(index, index + 4).map((stream) => this.fill(stream)));
    this.totalCount = this.streams.reduce((sum, stream) => sum + stream.total, 0);
  }

  private async initialize() {
    if (this.initialized) return;
    this.streams = this.sources.map((source) => ({
      source,
      page: 0,
      queue: [],
      exhausted: !source.available,
      total: 0,
    }));
    for (const source of this.sources.filter((source) => !source.available))
      this.failures.set(source.serverId, new Error(source.error || "Device unavailable"));
    // Bounded device fan-out as well as bounded pages.
    await this.fillPending(this.streams);
    this.initialized = true;
  }

  page(number: number): Promise<ResourcePageResult> {
    // Multiple navigation requests may share one pager. Advancing its streams
    // concurrently would consume rows twice or skip rows between display pages.
    const request = this.pageRequests.then(() => this.loadPage(number));
    this.pageRequests = request.then(
      () => undefined,
      () => undefined,
    );
    return request;
  }

  private async loadPage(number: number): Promise<ResourcePageResult> {
    number = Math.max(1, Math.floor(number));
    if (this.sources.length === 1) {
      this.checkCancelled();
      const source = this.sources[0];
      if (!source.available) {
        this.failures.set(source.serverId, new Error(source.error || "Device unavailable"));
        return { data: [], totalCount: 0 };
      }
      const pageSize = this.form.pageSize > 0 ? this.form.pageSize : 50;
      const offset = (number - 1) * pageSize;
      const data: Resource[] = [];
      // One owner's ordering is already final. Jump directly to its native page,
      // rather than transferring every earlier page of a large library.
      try {
        for (let position = offset; data.length < pageSize; ) {
          const nativePage = Math.floor(position / batchSize) + 1;
          let result = this.nativePages.get(nativePage);
          if (!result) {
            result = await this.request(
              source,
              { ...this.form, page: nativePage, pageSize: batchSize },
              this.signal,
            );
            this.checkCancelled();
            this.nativePages.set(nativePage, result);
            if (this.nativePages.size > cachedPageLimit)
              this.nativePages.delete(this.nativePages.keys().next().value!);
          }
          this.checkCancelled();
          this.totalCount = result.totalCount;
          const items = result.data.slice(
            position % batchSize,
            (position % batchSize) + pageSize - data.length,
          );
          data.push(...items.map((resource) => ({ ...resource, owner: source })));
          position += items.length;
          if (!items.length || position >= result.totalCount) break;
        }
        this.failures.delete(source.serverId);
        return { data, totalCount: this.totalCount };
      } catch (error) {
        this.checkCancelled();
        this.failures.set(
          source.serverId,
          error instanceof Error ? error : new Error(String(error)),
        );
        return { data: [], totalCount: 0 };
      }
    }
    await this.initialize();
    this.checkCancelled();
    if (number <= this.consumedPages && !this.pages.has(number)) {
      this.pages.clear();
      this.consumedPages = 0;
      this.initialized = false;
      await this.initialize();
    }
    while (this.consumedPages < number) {
      const page: Resource[] = [];
      const seen = new Set<string | number>();
      for (let index = 0; index < (this.form.pageSize > 0 ? this.form.pageSize : 50); index++) {
        this.checkCancelled();
        await this.fillPending(
          this.streams.filter((stream) => !stream.queue.length && !stream.exhausted),
        );
        const next = this.streams
          .filter((stream) => stream.queue.length)
          .reduce<
            Stream | undefined
          >((best, stream) => (!best || compareOwnedResources(stream.queue[0], best.queue[0], this.form.orders) < 0 ? stream : best), undefined);
        if (!next) break;
        const resource = next.queue.shift()!;
        const key = resourceSelectionKey(resource);
        if (seen.has(key)) {
          index--;
          continue;
        }
        seen.add(key);
        page.push(resource);
      }
      this.consumedPages++;
      this.pages.set(this.consumedPages, page);
      if (this.pages.size > cachedPageLimit) this.pages.delete(this.pages.keys().next().value!);
      if (!page.length) break;
    }
    return { data: this.pages.get(number) ?? [], totalCount: this.totalCount };
  }
}
