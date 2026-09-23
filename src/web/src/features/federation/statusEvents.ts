const eventName = "bakabase:federation-browsing-changed";
const channelName = "bakabase-federation-status";
const storageKey = "federation.status-change";
const sender = `${Date.now()}:${Math.random()}`;

interface BrowsingChange {
  sender: string;
  enabled: boolean;
}

/** Notifications are hints to refresh local status, never a source of permission to enable browsing. */
export function notifyBrowsingChanged(enabled: boolean) {
  const message: BrowsingChange = { sender, enabled };

  window.dispatchEvent(new CustomEvent(eventName, { detail: message }));
  if (typeof window.BroadcastChannel === "function") {
    const channel = new window.BroadcastChannel(channelName);

    channel.postMessage(message);
    channel.close();
  } else {
    try {
      localStorage.setItem(storageKey, JSON.stringify({ ...message, changedAt: Date.now() }));
    } catch {
      // Focus/visibility refresh still observes changes when browser storage is unavailable.
    }
  }
}

export function subscribeBrowsingChanged(listener: (enabled: boolean) => void) {
  const receive = (message: unknown, remote: boolean) => {
    if (!message || typeof message !== "object") return;
    const change = message as Partial<BrowsingChange>;

    if (typeof change.enabled === "boolean" && (!remote || change.sender !== sender))
      listener(change.enabled);
  };
  const onLocal = (event: Event) => receive((event as CustomEvent).detail, false);
  const onStorage = (event: StorageEvent) => {
    if (event.key !== storageKey || !event.newValue) return;
    try {
      receive(JSON.parse(event.newValue), true);
    } catch {
      /* Ignore unrelated malformed storage events. */
    }
  };
  const channel =
    typeof window.BroadcastChannel === "function"
      ? new window.BroadcastChannel(channelName)
      : undefined;

  if (channel) channel.onmessage = (event) => receive(event.data, true);
  window.addEventListener(eventName, onLocal);
  window.addEventListener("storage", onStorage);

  return () => {
    channel?.close();
    window.removeEventListener(eventName, onLocal);
    window.removeEventListener("storage", onStorage);
  };
}
