import { create } from "zustand";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";

/**
 * Which program is answering as PureClient.
 *
 * - `console` — the desktop app showing a server it manages, through its own relay. Full
 *   control of that server; the app itself, its updater and its tray are this device's.
 * - `legacy` — the retired Bakabase Client.
 */
export type ClientHost = "console" | "legacy";

interface IRemoteAccessState {
  /** False until the first answer from the server arrives. */
  initialized: boolean;
  /**
   * Whether this browser is on the machine running Bakabase. Answered by the
   * server from the connection itself, not guessed from the URL — opening
   * `http://192.168.1.5:34567` on the host is still local, and a reverse proxy
   * would make any URL-based guess wrong anyway.
   *
   * Not the same question as {@link clientMode}. A thin client is not local —
   * its files really are elsewhere — yet it can still launch a player.
   */
  isLocal: boolean;
  mode: RemoteAccessMode;
  /**
   * Which flavour is answering. A thin client's forwarding layer answers this
   * endpoint itself, which is the only way `PureClient` ever appears.
   */
  clientMode: ClientMode;
  /**
   * False while a thin client cannot reach its server. Always true from a
   * server, which answered by definition.
   */
  serverReachable: boolean;
  /** Whether a sign-in capture window can open for this caller. */
  cookieCaptureAvailable: boolean;
  serverName?: string;
  /**
   * Only ever set under PureClient, once `/client/status` has answered. Undefined until
   * then — callers that behave differently per host wait rather than guess, because the
   * two guesses are wrong in opposite, visible ways (a retired client's updater offered
   * in the desktop app, or the desktop app's switcher missing from the client).
   */
  clientHost?: ClientHost;
  /** In the console: this device's own name, as opposed to the server being shown. */
  localName?: string;
  /**
   * Under PureClient, once `/client/status` has answered: the id the server being shown
   * knows this window's device by. Its list of paired devices contains this one, and
   * revoking it is the one revocation that also ends the session doing it.
   */
  ownDeviceId?: string;
  load: () => Promise<void>;
}

/**
 * Asks the PureClient which program it is. The console says so; the retired client
 * predates the question and says nothing, which is itself the answer.
 */
const resolveClientHost = async (): Promise<
  Pick<IRemoteAccessState, "clientHost" | "localName" | "ownDeviceId">
> => {
  const status = await clientApi.status();
  const active =
    status?.servers?.find((server) => server.isActive) ??
    status?.servers?.find((server) => server.serverId === status.activeServerId);
  const ownDeviceId = active?.deviceId || undefined;

  return status?.host === "console"
    ? { clientHost: "console", localName: status.localName || undefined, ownDeviceId }
    : { clientHost: "legacy", localName: undefined, ownDeviceId };
};

export const useRemoteAccessStore = create<IRemoteAccessState>((set) => ({
  initialized: false,
  // Assume local until told otherwise: the desktop app is the overwhelmingly
  // common case, and it must not flicker through a "remote" rendering on start.
  isLocal: true,
  mode: RemoteAccessMode.Disabled,
  clientMode: ClientMode.AllInOne,
  serverReachable: true,
  cookieCaptureAvailable: true,
  load: async () => {
    try {
      const rsp = await BApi.remoteAccess.getRemoteAccessContext();
      const data = rsp.data;

      if (data) {
        const isLocal = data.isLocal ?? true;
        // A backend that predates the field is an all-in-one when the caller
        // is on it and an ordinary remote browser otherwise. Neither can be
        // PureClient — that answer only ever comes from a client that
        // intercepts this endpoint.
        const clientMode =
          data.clientMode ?? (isLocal ? ClientMode.AllInOne : ClientMode.RemoteBrowser);

        set({
          initialized: true,
          isLocal,
          mode: data.mode ?? RemoteAccessMode.Disabled,
          clientMode,
          serverReachable: data.serverReachable ?? true,
          cookieCaptureAvailable: data.cookieCaptureAvailable ?? isLocal,
          serverName: data.serverName ?? undefined,
          ...(clientMode === ClientMode.PureClient
            ? {}
            : { clientHost: undefined, localName: undefined, ownDeviceId: undefined }),
        });

        if (clientMode === ClientMode.PureClient) {
          try {
            set(await resolveClientHost());
          } catch {
            // Its own status route not answering means the process is going away; the
            // next load asks again. Until then nothing host-specific is shown.
          }
        }
      }
    } catch {
      // An older backend, or a request that failed on a flaky LAN. Staying
      // local-by-default keeps the desktop app working; a genuinely remote
      // device would have been refused by the gate long before this point.
      set({ initialized: true });
    }
  },
}));

/**
 * True when the UI is being used from another device, so the files it shows are
 * not on this machine.
 *
 * This asks about *files*, not about *actions* — a thin client is a remote
 * client by this measure and can still launch a player. Use
 * {@link useUserSideActionsRunHere} for anything that runs a program, opens a
 * folder, or shows a window.
 */
export const useIsRemoteClient = () =>
  useRemoteAccessStore((state) => state.initialized && !state.isLocal);

/**
 * True when an action that has to happen on a person's own machine will happen
 * on *this* one.
 *
 * Two flavours qualify and they qualify for different reasons: the all-in-one,
 * because the server is this machine; the thin client, because it intercepts
 * those endpoints and runs them here. An ordinary browser pointed at a server
 * qualifies for neither, and there the action would land on a screen nobody is
 * watching.
 */
export const useUserSideActionsRunHere = () =>
  useRemoteAccessStore(
    (state) => state.initialized && (state.isLocal || state.clientMode === ClientMode.PureClient),
  );

/**
 * True when `/client/*` is answered on this origin: the retired thin client, or the
 * desktop app's console showing a managed server. Both run user-side actions here; see
 * {@link useIsConsole} / {@link useIsLegacyClient} for anything that differs.
 */
export const useIsPureClient = () =>
  useRemoteAccessStore((state) => state.initialized && state.clientMode === ClientMode.PureClient);

/**
 * True when this window is the desktop app showing a server it manages. The UI is that
 * server's own; the window, updater and tray belong to the device the app runs on.
 */
export const useIsConsole = () =>
  useRemoteAccessStore(
    (state) =>
      state.initialized &&
      state.clientMode === ClientMode.PureClient &&
      state.clientHost === "console",
  );

/** True in the retired Bakabase Client, once it has been told apart from the console. */
export const useIsLegacyClient = () =>
  useRemoteAccessStore(
    (state) =>
      state.initialized &&
      state.clientMode === ClientMode.PureClient &&
      state.clientHost === "legacy",
  );

/**
 * True when this window may administer the server it shows — who else may manage it,
 * above all: sitting at it, a paired device (the console or the retired client, whose
 * requests are signed), or a browser the server lets in unconditionally because its mode
 * is Unrestricted. An ordinary browser on a paired-only server can read, not decide.
 */
export const useCanAdministerShownServer = () =>
  useRemoteAccessStore(
    (state) =>
      state.initialized &&
      (state.isLocal ||
        state.clientMode === ClientMode.PureClient ||
        state.mode === RemoteAccessMode.Unrestricted),
  );

/**
 * True when a sign-in capture window can open. In the thin client that window
 * belongs to this process, so it opens here with this machine's browser and
 * this person's cookies — which is why the client reports it available even
 * though its server is elsewhere.
 */
export const useCookieCaptureAvailable = () =>
  useRemoteAccessStore((state) => state.initialized && state.cookieCaptureAvailable);
