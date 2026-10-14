import type { PlayableItem } from "@/core/models/Resource";
import type { DataOrigin, ResourceDataType } from "@/sdk/constants";

import envConfig from "@/config/env";

export type DiscoveryData = {
  origin: DataOrigin;
  dataType: ResourceDataType;
  coverPaths?: string[];
  playableItems?: PlayableItem[];
};

export type DiscoveryResult = {
  resourceId: number;
  origin: DataOrigin;
  dataType: ResourceDataType;
  success: boolean;
  coverPaths?: string[];
  playableItems?: PlayableItem[];
  error?: string;
};

type SubscriberKey = `${number}-${DataOrigin}-${ResourceDataType}`;
type Subscriber = (data: DiscoveryData | null, error?: string) => void;

type PendingSubscription = {
  resourceId: number;
  origin: number;
  dataType: number;
};

export class ResourceDiscoveryChannel {
  private eventSource: EventSource | null = null;
  private subscribers = new Map<SubscriberKey, Set<Subscriber>>();
  private pendingRequests = new Set<string>(); // resourceId-origin-dataType (already sent)
  private pendingSubscriptions = new Map<string, PendingSubscription>(); // to be sent in batch
  private connectionPromise: Promise<void> | null = null;
  private batchTimeout: ReturnType<typeof setTimeout> | null = null;
  private connectionTimeout: ReturnType<typeof setTimeout> | null = null;

  // Batch delay in ms - requests within this window will be merged
  private readonly batchDelay = 50;

  private getApiEndpoint(): string {
    return envConfig.apiEndpoint || "";
  }

  private ensureConnected(): Promise<void> {
    if (this.eventSource?.readyState === EventSource.OPEN) {
      return Promise.resolve();
    }

    if (this.connectionPromise) {
      return this.connectionPromise;
    }

    // Replace a transport still reconnecting from an earlier failure. A new
    // retry must not leave two live EventSources delivering the same results.
    this.eventSource?.close();
    this.eventSource = null;
    this.connectionPromise = new Promise((resolve, reject) => {
      const endpoint = this.getApiEndpoint();
      const url = `${endpoint}/resource/discovery/stream`;

      console.log("[ResourceDiscoveryChannel] Connecting to SSE:", url);

      const source = new EventSource(url);
      let connected = false;

      this.eventSource = source;
      const failConnection = (message: string) => {
        if (this.eventSource !== source) return;
        this.clearConnectionTimeout();
        this.connectionPromise = null;
        source.close();
        this.eventSource = null;
        reject(new Error(message));
      };

      this.connectionTimeout = setTimeout(
        () => failConnection("Discovery connection timed out"),
        15000,
      );
      source.onopen = () => {
        if (this.eventSource !== source) return;
        console.log("[ResourceDiscoveryChannel] SSE connection established");
        connected = true;
        this.clearConnectionTimeout();
        this.connectionPromise = null;
        resolve();
      };

      source.onerror = (event) => {
        if (this.eventSource !== source) return;
        // The backend also uses an SSE event named "error" for one failed
        // discovery. It is not a broken transport and must not fail other jobs.
        if (event instanceof MessageEvent) return;
        if (!connected) {
          failConnection("Could not connect to file discovery");

          return;
        }
        for (const key of this.pendingRequests) {
          this.pendingRequests.delete(key);
          this.subscribers
            .get(key as SubscriberKey)
            ?.forEach((callback) => callback(null, "File discovery connection was interrupted"));
        }
        // EventSource reconnects CONNECTING transports itself. Closed transports
        // are replaced on the next request; their pending jobs have a visible error.
        if (source.readyState === EventSource.CLOSED) {
          source.close();
          if (this.eventSource === source) this.eventSource = null;
        }
      };

      // Listen for result events
      this.eventSource.addEventListener("result", (event) => {
        try {
          const result = JSON.parse(event.data) as DiscoveryResult;

          this.notifySubscribers(result);
        } catch (e) {
          console.error("[ResourceDiscoveryChannel] Failed to parse result:", e);
        }
      });

      // Listen for error events
      this.eventSource.addEventListener("error", (event) => {
        if (event instanceof MessageEvent) {
          try {
            const result = JSON.parse(event.data) as DiscoveryResult;

            this.notifySubscribers(result);
          } catch (e) {
            console.error("[ResourceDiscoveryChannel] Failed to parse error:", e);
          }
        }
      });
    });

    return this.connectionPromise;
  }

  private clearConnectionTimeout() {
    if (this.connectionTimeout) {
      clearTimeout(this.connectionTimeout);
      this.connectionTimeout = null;
    }
  }

  private notifySubscribers(result: DiscoveryResult) {
    const key: SubscriberKey = `${result.resourceId}-${result.origin}-${result.dataType}`;

    this.pendingRequests.delete(key);
    const subscribers = this.subscribers.get(key);

    if (!subscribers || subscribers.size === 0) return;

    console.log(
      "[ResourceDiscoveryChannel] Notifying",
      subscribers.size,
      "subscribers for resource",
      result.resourceId,
      "origin",
      result.origin,
      "dataType",
      result.dataType,
    );

    if (result.success) {
      const data: DiscoveryData = {
        origin: result.origin,
        dataType: result.dataType,
        coverPaths: result.coverPaths,
        playableItems: result.playableItems,
      };

      subscribers.forEach((cb) => cb(data));
    } else {
      subscribers.forEach((cb) => cb(null, result.error));
    }
  }

  private scheduleBatchSend() {
    if (this.batchTimeout) {
      return; // Already scheduled
    }

    this.batchTimeout = setTimeout(() => {
      this.batchTimeout = null;
      this.sendBatch();
    }, this.batchDelay);
  }

  private async sendBatch() {
    if (this.pendingSubscriptions.size === 0) {
      return;
    }

    const requests = Array.from(this.pendingSubscriptions.values());
    const requestKeys = Array.from(this.pendingSubscriptions.keys());

    // Clear pending subscriptions
    this.pendingSubscriptions.clear();

    // Mark as sent
    requestKeys.forEach((key) => this.pendingRequests.add(key));

    console.log("[ResourceDiscoveryChannel] Sending batch of", requests.length, "subscriptions");

    const endpoint = this.getApiEndpoint();
    const requestController = new AbortController();
    const requestTimeout = setTimeout(() => requestController.abort(), 15000);

    try {
      const response = await fetch(`${endpoint}/resource/discovery/subscribe/batch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(requests),
        signal: requestController.signal,
      });

      if (!response.ok) throw new Error(`Discovery request failed (${response.status})`);
      const result = (await response.json()) as { code?: number; message?: string };

      if (result.code)
        throw new Error(result.message || `Discovery request failed (${result.code})`);
    } catch (e) {
      console.error("[ResourceDiscoveryChannel] Failed to send batch:", e);
      // A result may arrive over SSE before this HTTP response fails. Preserve
      // that completed result; only requests still waiting need an error.
      const failedRequests = requests.filter((req) =>
        this.pendingRequests.has(`${req.resourceId}-${req.origin}-${req.dataType}`),
      );

      requestKeys.forEach((key) => this.pendingRequests.delete(key));
      failedRequests.forEach((req) => {
        const key: SubscriberKey =
          `${req.resourceId}-${req.origin}-${req.dataType}` as SubscriberKey;
        const subs = this.subscribers.get(key);

        if (subs) {
          const message = requestController.signal.aborted
            ? "Discovery request timed out"
            : e instanceof Error
              ? e.message
              : "Failed to subscribe";

          subs.forEach((cb) => cb(null, message));
        }
      });
    } finally {
      clearTimeout(requestTimeout);
    }
  }

  async subscribe(
    resourceId: number,
    origin: DataOrigin,
    dataType: ResourceDataType,
    callback: Subscriber,
  ): Promise<() => void> {
    const key: SubscriberKey = `${resourceId}-${origin}-${dataType}`;

    // Register subscriber
    if (!this.subscribers.has(key)) {
      this.subscribers.set(key, new Set());
    }
    this.subscribers.get(key)!.add(callback);

    let connected = false;

    try {
      await this.ensureConnected();
      connected = true;
    } catch (error) {
      callback(null, error instanceof Error ? error.message : "Failed to connect");
    }

    const requestKey = `${resourceId}-${origin}-${dataType}`;

    // Add to batch if not already pending or sent
    if (
      connected &&
      !this.pendingRequests.has(requestKey) &&
      !this.pendingSubscriptions.has(requestKey)
    ) {
      this.pendingSubscriptions.set(requestKey, { resourceId, origin, dataType });
      this.scheduleBatchSend();
    }

    // Return unsubscribe function
    return () => {
      const subs = this.subscribers.get(key);

      if (subs) {
        subs.delete(callback);
        if (subs.size === 0) {
          this.subscribers.delete(key);
          // Another card/detail may share this request. Removing one listener
          // must not remove the other listener's pending batch.
          this.pendingRequests.delete(requestKey);
          this.pendingSubscriptions.delete(requestKey);
        }
      }

      // Disconnect if no more subscribers
      if (this.subscribers.size === 0) {
        this.disconnect();
      }
    };
  }

  disconnect() {
    console.log("[ResourceDiscoveryChannel] Disconnecting");

    if (this.batchTimeout) {
      clearTimeout(this.batchTimeout);
      this.batchTimeout = null;
    }

    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }

    this.connectionPromise = null;
    this.clearConnectionTimeout();
    this.pendingRequests.clear();
    this.pendingSubscriptions.clear();
  }

  // Get connection status for debugging
  get isConnected(): boolean {
    return this.eventSource?.readyState === EventSource.OPEN;
  }

  get subscriberCount(): number {
    return this.subscribers.size;
  }
}

// Export singleton instance
export const resourceDiscoveryChannel = new ResourceDiscoveryChannel();

export default resourceDiscoveryChannel;
