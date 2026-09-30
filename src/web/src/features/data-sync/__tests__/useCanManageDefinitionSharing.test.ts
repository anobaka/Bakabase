import { renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { useDataSyncWindow } from "../hooks/useDataSyncWindow";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * Who may create definitions access (spec §7.1.5): this device's own window, and the desktop
 * app's window showing a server it manages — never a browser the server lets in only because
 * it is Unrestricted. And who may use the page at all.
 */

const initial = useRemoteAccessStore.getState();

afterEach(() => useRemoteAccessStore.setState(initial, true));

const as = (state: Partial<ReturnType<typeof useRemoteAccessStore.getState>>) =>
  useRemoteAccessStore.setState({ initialized: true, context: "known", ...state });

const canManage = () => renderHook(() => useCanManageDefinitionSharing()).result.current;
const reach = () => renderHook(() => useDataSyncWindow()).result.current;

describe("who may create definitions access", () => {
  it("is this device's own window", () => {
    as({ isLocal: true, clientMode: ClientMode.AllInOne, mode: RemoteAccessMode.Disabled });
    expect(canManage()).toBe(true);
  });

  it("is the desktop app's window showing a server it manages", () => {
    as({ isLocal: false, clientMode: ClientMode.PureClient, mode: RemoteAccessMode.Enabled });
    expect(canManage()).toBe(true);
  });

  it("is never an Unrestricted browser", () => {
    as({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
    });
    expect(canManage()).toBe(false);
  });

  it("is no one before the server said who is looking", () => {
    useRemoteAccessStore.setState({ initialized: false, context: "asking" });
    expect(canManage()).toBe(false);
  });
});

describe("who may use the page", () => {
  it("waits for the server to say who is looking", () => {
    useRemoteAccessStore.setState({ initialized: false, context: "asking" });
    expect(reach()).toBe("asking");
  });

  it("lets this device's own window, the desktop app's and an Unrestricted browser in", () => {
    as({ isLocal: true, clientMode: ClientMode.AllInOne });
    expect(reach()).toBe("allowed");
    as({ isLocal: false, clientMode: ClientMode.PureClient, mode: RemoteAccessMode.Enabled });
    expect(reach()).toBe("allowed");
    as({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Unrestricted,
    });
    expect(reach()).toBe("allowed");
  });

  it("keeps a browser on a server outside Unrestricted mode out", () => {
    as({ isLocal: false, clientMode: ClientMode.RemoteBrowser, mode: RemoteAccessMode.Enabled });
    expect(reach()).toBe("notAllowed");
  });

  it("tries when who is looking could not be read", () => {
    useRemoteAccessStore.setState({ initialized: true, context: "unknown" });
    expect(reach()).toBe("unknown");
  });
});
