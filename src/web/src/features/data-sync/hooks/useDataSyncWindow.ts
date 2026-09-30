import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/**
 * Whether this window may use data sync, before asking data sync itself:
 * - `asking` — the server has not said who is looking yet;
 * - `allowed` — this device's own window, the desktop app's window showing a server it manages,
 *   or a browser the server lets in because its mode is Unrestricted;
 * - `notAllowed` — a browser on another device of a server outside Unrestricted mode. The
 *   server would refuse every request with 403 `HostOnly`, so none is made;
 * - `unknown` — who is looking could not be read. The page loads as usual, and a 403
 *   `HostOnly` then turns it into the same notice as `notAllowed`.
 */
export type DataSyncWindow = "asking" | "allowed" | "notAllowed" | "unknown";

export const useDataSyncWindow = (): DataSyncWindow =>
  useRemoteAccessStore((state) => {
    if (state.context === "asking") return "asking";
    if (state.context === "unknown") return "unknown";

    return state.isLocal ||
      state.clientMode === ClientMode.PureClient ||
      state.mode === RemoteAccessMode.Unrestricted
      ? "allowed"
      : "notAllowed";
  });
