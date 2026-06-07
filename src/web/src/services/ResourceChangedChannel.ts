/**
 * Lightweight, transient pub/sub for "these resource ids changed on the backend"
 * notifications pushed over the existing UI SignalR hub (key "Resource").
 *
 * Deliberately holds no per-resource state — only the set of currently-mounted
 * subscribers (typically the active resource tab). Each subscriber decides which of
 * the announced ids are relevant to it and reloads just those, so the resource list
 * refreshes after a cache rebuild without a full re-search and without a growing store.
 */
type Subscriber = (ids: number[]) => void;

class ResourceChangedChannel {
  private subscribers = new Set<Subscriber>();

  subscribe(callback: Subscriber): () => void {
    this.subscribers.add(callback);

    return () => {
      this.subscribers.delete(callback);
    };
  }

  publish(ids: number[]) {
    if (!ids || ids.length === 0) {
      return;
    }
    this.subscribers.forEach((cb) => cb(ids));
  }
}

export const resourceChangedChannel = new ResourceChangedChannel();

export default resourceChangedChannel;
