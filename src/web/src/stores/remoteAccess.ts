import { create } from "zustand";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";

/**
 * Which program is answering as PureClient. Only one is known: `console`, the desktop app
 * showing a server it manages, through its own relay — full control of that server, while
 * the app itself, its updater and its tray are this device's.
 */
export type ClientHost = "console";

interface IRemoteAccessState {
  /** False until the first answer from the server arrives. */
  initialized: boolean;
  /**
   * Whether this browser is on the machine running Bakabase. Answered by the
   * server from the connection itself, not guessed from the URL — opening
   * `http://192.168.1.5:34567` on the host is still local, and a reverse proxy
   * would make any URL-based guess wrong anyway.
   *
   * Not the same question as {@link clientMode}. The desktop app showing a server it
   * manages is not local — that server's files really are elsewhere — yet it can still
   * launch a player.
   */
  isLocal: boolean;
  mode: RemoteAccessMode;
  /**
   * Which flavour is answering. The desktop app's relay for a managed server answers
   * this endpoint itself, which is the only way `PureClient` ever appears.
   */
  clientMode: ClientMode;
  /**
   * False while a relay cannot reach its server. Always true from a server, which
   * answered by definition.
   */
  serverReachable: boolean;
  /** Whether a sign-in capture window can open for this caller. */
  cookieCaptureAvailable: boolean;
  serverName?: string;
  /**
   * Only ever set under PureClient, once `/client/status` has answered and said it is the
   * console. Undefined until then, and for anything else answering there — callers that
   * need the console wait rather than guess.
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
 * Asks the PureClient which program it is. The console says so; nothing else is known.
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
    : { clientHost: undefined, localName: undefined, ownDeviceId };
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
 * This asks about *files*, not about *actions* — the desktop app showing a server it
 * manages is a remote client by this measure and can still launch a player. Use
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
 * because the server is this machine; the desktop app showing a server it manages,
 * because its relay intercepts those endpoints and runs them here. An ordinary browser pointed at a server
 * qualifies for neither, and there the action would land on a screen nobody is
 * watching.
 */
export const useUserSideActionsRunHere = () =>
  useRemoteAccessStore(
    (state) => state.initialized && (state.isLocal || state.clientMode === ClientMode.PureClient),
  );

/**
 * True when `/client/*` is answered on this origin: the desktop app's console showing a
 * managed server, which runs user-side actions here. Known as soon as the context call
 * answers; {@link useIsConsole} waits for `/client/status` to confirm the console.
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

/**
 * True when this window may administer the server it shows — who else may manage it,
 * above all: sitting at it, a paired device (the console, whose requests are signed), or
 * a browser the server lets in unconditionally because its mode is Unrestricted. An
 * ordinary browser on a paired-only server can read, not decide.
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
 * True when a sign-in capture window can open. In the console that window belongs
 * to the desktop app, so it opens here with this machine's browser and this
 * person's cookies — which is why the relay reports it available even though its
 * server is elsewhere.
 */
export const useCookieCaptureAvailable = () =>
  useRemoteAccessStore((state) => state.initialized && state.cookieCaptureAvailable);
