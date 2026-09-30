import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/**
 * Whether this window may create or widen definitions access (spec §7.1.5): turn sharing on,
 * approve a request, create a code, send a request. This device's own window may, and so may
 * the desktop app's window showing a server it manages — its relay signs every request as a
 * paired device. A browser on another device may not, even one the server lets in because its
 * mode is Unrestricted: nobody approved it, and a credential it minted would outlive a later
 * switch to requiring pairing.
 *
 * The same predicate as `useUserSideActionsRunHere`, deliberately not
 * `useCanAdministerShownServer`, which also admits Unrestricted browsers. The server's
 * `DataSyncOverview.canManageSharing` is what counts; this only hides the controls early.
 */
export const useCanManageDefinitionSharing = () =>
  useRemoteAccessStore(
    (state) => state.initialized && (state.isLocal || state.clientMode === ClientMode.PureClient),
  );
