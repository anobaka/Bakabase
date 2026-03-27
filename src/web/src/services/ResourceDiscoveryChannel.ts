import envConfig from "@/config/env";
import { DataOrigin, ResourceDataType } from "@/sdk/constants";
import type { PlayableItem } from "@/core/models/Resource";

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

class ResourceDiscoveryChannel {
  private eventSource: EventSource | null = null;
  private subscribers = new Map<SubscriberKey, Set<Subscriber>>();
  private pendingRequests = new Set<string>(); // resourceId-origin-dataType (already sent)
  private pendingSubscriptions = new Map<string, PendingSubscription>(); // to be sent in batch
  private connectionPromise: Promise<void> | null = null;
  private reconnectTimeout: ReturnType<typeof setTimeout> | null = null;
  private batchTimeout: ReturnType<typeof setTimeout> | null = null;
  private isConnecting = false;

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

    this.isConnecting = true;

    this.connectionPromise = new Promise((resolve, reject) => {
      const endpoint = this.getApiEndpoint();
      const url = `${endpoint}/resource/discovery/stream`;

      console.log("[ResourceDiscoveryChannel] Connecting to SSE:", url);

      this.eventSource = new EventSource(url);

      this.eventSource.onopen = () => {
        console.log("[ResourceDiscoveryChannel] SSE connection established");
        this.isConnecting = false;
        this.connectionPromise = null;
        resolve();
      };

      this.eventSource.onerror = (event) => {
        console.error("[ResourceDiscoveryChannel] SSE connection error:", event);
        this.isConnecting = false;
        this.connectionPromise = null;

        // If we were connecting, reject the promise
        if (this.eventSource?.readyState === EventSource.CONNECTING) {
          // Still trying to connect, let EventSource handle it
          return;
        }

        // Connection closed, schedule reconnect
        this.scheduleReconnect();
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

  private scheduleReconnect() {
    if (this.reconnectTimeout) {
      return;
    }

    console.log("[ResourceDiscoveryChannel] Scheduling reconnect in 3 seconds");

    this.reconnectTimeout = setTimeout(() => {
      this.reconnectTimeout = null;

      // Only reconnect if we still have subscribers
      if (this.subscribers.size > 0) {
        console.log("[ResourceDiscoveryChannel] Reconnecting...");
        this.ensureConnected();
      }
    }, 3000);
  }

  private notifySubscribers(result: DiscoveryResult) {
    const key: SubscriberKey = `${result.resourceId}-${result.origin}-${result.dataType}`;
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
      result.dataType
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
    try {
      await fetch(`${endpoint}/resource/discovery/subscribe/batch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(requests),
      });
    } catch (e) {
      console.error("[ResourceDiscoveryChannel] Failed to send batch:", e);
      // Remove from pending on failure so they can be retried
      requestKeys.forEach((key) => this.pendingRequests.delete(key));

      // Notify subscribers of error
      requests.forEach((req) => {
        const key: SubscriberKey = `${req.resourceId}-${req.origin}-${req.dataType}` as SubscriberKey;
        const subs = this.subscribers.get(key);
        if (subs) {
          subs.forEach((cb) => cb(null, "Failed to subscribe"));
        }
      });
    }
  }

  async subscribe(
    resourceId: number,
    origin: DataOrigin,
    dataType: ResourceDataType,
    callback: Subscriber
  ): Promise<() => void> {
    const key: SubscriberKey = `${resourceId}-${origin}-${dataType}`;

    // Register subscriber
    if (!this.subscribers.has(key)) {
      this.subscribers.set(key, new Set());
    }
    this.subscribers.get(key)!.add(callback);

    // Ensure connection is established
    await this.ensureConnected();

    const requestKey = `${resourceId}-${origin}-${dataType}`;

    // Add to batch if not already pending or sent
    if (!this.pendingRequests.has(requestKey) && !this.pendingSubscriptions.has(requestKey)) {
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
        }
      }

      // Clear from pending
      this.pendingRequests.delete(requestKey);
      this.pendingSubscriptions.delete(requestKey);

      // Disconnect if no more subscribers
      if (this.subscribers.size === 0) {
        this.disconnect();
      }
    };
  }

  disconnect() {
    console.log("[ResourceDiscoveryChannel] Disconnecting");

    if (this.reconnectTimeout) {
      clearTimeout(this.reconnectTimeout);
      this.reconnectTimeout = null;
    }

    if (this.batchTimeout) {
      clearTimeout(this.batchTimeout);
      this.batchTimeout = null;
    }

    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }

    this.connectionPromise = null;
    this.isConnecting = false;
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
