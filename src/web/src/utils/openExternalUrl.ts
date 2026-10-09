import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/** Browser links must not execute scripts or open server-local files. */
export function browserExternalUrl(value: string): string | undefined {
  try {
    const url = new URL(value);

    return url.protocol === "https:" || url.protocol === "http:" ? url.href : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Call directly from a click/press handler: opening synchronously preserves browser
 * user activation. Desktop windows and managed-server relays keep their OS opener.
 */
export function openExternalUrl(url: string): void {
  if (useRemoteAccessStore.getState().clientMode !== ClientMode.RemoteBrowser) {
    void BApi.gui.openUrlInDefaultBrowser({ url });

    return;
  }

  const href = browserExternalUrl(url);

  if (!href) return;
  const link = document.createElement("a");

  link.href = href;
  link.target = "_blank";
  link.rel = "noopener noreferrer";
  document.body.append(link);
  link.click();
  link.remove();
}
