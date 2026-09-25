import { useCallback, useEffect, useState } from "react";

import { DATA_SYNC_ROUTE } from "../routes";

import { clientApi } from "@/core/clientApi";
import { managedServerApi } from "@/features/federation/serverApi";
import { openConsoleTarget, openManagedServer } from "@/features/federation/switching";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/**
 * Whether this window can switch to another device's own Data sync page, and the way there
 * (spec §9.1 N): a device that holds decisions nobody has taken there is decided there.
 *
 * This device's own window switches to the servers it manages; the desktop app's window
 * showing a managed server switches through its relay's switcher. A device is found by its
 * install id — a peer's node id is its remote-access server id unless it was reset. Anywhere
 * else, or for a device this one does not manage, nothing is offered and the reader is told
 * where to go instead.
 */
export function useOpenPeerDataSync(wanted: boolean) {
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const isLocal = useRemoteAccessStore((state) => state.isLocal);
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const clientHost = useRemoteAccessStore((state) => state.clientHost);
  const inConsole = initialized && clientMode === ClientMode.PureClient && clientHost === "console";
  const own = initialized && isLocal && clientMode !== ClientMode.PureClient;
  const [targets, setTargets] = useState<Set<string>>();

  useEffect(() => {
    if (!wanted || (!inConsole && !own)) return;
    let live = true;
    const read = inConsole
      ? clientApi.switcher
          .list()
          .then((switcher) =>
            switcher.targets.filter((target) => !target.isLocal).map((target) => target.id),
          )
      : managedServerApi.list(false).then((view) => view.servers.map((server) => server.serverId));

    read.then(
      (ids) => live && setTargets(new Set(ids)),
      () => live && setTargets(new Set()),
    );

    return () => {
      live = false;
    };
  }, [wanted, inConsole, own]);

  const canOpen = useCallback((nodeId: string) => !!targets?.has(nodeId), [targets]);
  const open = useCallback(
    (nodeId: string) =>
      inConsole
        ? openConsoleTarget(nodeId, DATA_SYNC_ROUTE)
        : openManagedServer(nodeId, DATA_SYNC_ROUTE),
    [inConsole],
  );

  return { canOpen, open };
}
