const eventName = "bakabase:federation-status-changed";
const channelName = "bakabase-federation-status";
const storageKey = "federation.status-change";
const sender = `${Date.now()}:${Math.random()}`;

interface StatusChange {
  sender: string;
  kind: "changed" | "reset";
}

/** Notifications invalidate local state; permissions always come from the next server response. */
export function notifyFederationChanged(kind: StatusChange["kind"] = "changed") {
  const message: StatusChange = { sender, kind };

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

export function subscribeFederationChanged(listener: (kind: StatusChange["kind"]) => void) {
  const receive = (message: unknown, remote: boolean) => {
    if (!message || typeof message !== "object") return;
    const change = message as Partial<StatusChange>;

    if (
      typeof change.sender === "string" &&
      (change.kind === "changed" || change.kind === "reset") &&
      (!remote || change.sender !== sender)
    )
      listener(change.kind);
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
