"use client";

import type { ClientPairingTicket, ClientStatus } from "@/core/clientApi";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import ServerUpdateNotice from "./ServerUpdateNotice";

import MigrationNotice from "@/features/federation/components/MigrationNotice";
import { clientApi } from "@/core/clientApi";
import { ClientPairingOutcome, ServerHandshakeOutcome } from "@/sdk/constants";
import { Button, Chip, Input, Modal, Snippet } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useIsConsole, useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";
import { devicesRoute, openLocalView } from "@/features/federation/switching";

/**
 * Which server this client talks to, and how it got permission to.
 *
 * Only reachable in the thin client — the endpoints behind it exist nowhere else. The
 * page renders a plain notice in any other flavour rather than a broken screen, because
 * a bookmark or a stale tab can still land here.
 */

/** While waiting for somebody to approve, ask again on this cadence. */
const ClaimPollInterval = 3000;

const ClientConnectionPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isPureClient = useIsPureClient();
  const isConsole = useIsConsole();
  const reloadContext = useRemoteAccessStore((state) => state.load);

  const [status, setStatus] = useState<ClientStatus>();
  const [address, setAddress] = useState("");
  const [busy, setBusy] = useState(false);
  const [ticket, setTicket] = useState<ClientPairingTicket & { address: string }>();
  const claiming = useRef(false);

  const load = useCallback(async () => {
    try {
      setStatus(await clientApi.status());
    } catch {
      // Not the client, or the client is shutting down. The notice below covers it.
      setStatus(undefined);
    }
  }, []);

  useEffect(() => {
    if (isPureClient && !isConsole) {
      load();
    }
  }, [isPureClient, isConsole, load]);

  // Polling only while a request is outstanding; nothing runs otherwise.
  useEffect(() => {
    if (!ticket?.requestId) {
      return;
    }

    const timer = setInterval(async () => {
      if (claiming.current) {
        return;
      }
      claiming.current = true;
      try {
        const result = await clientApi.claimPairing(ticket.address, ticket.requestId!);

        if (result.outcome === ClientPairingOutcome.AwaitingApproval) {
          return;
        }

        setTicket(undefined);

        if (result.outcome === ClientPairingOutcome.Paired) {
          toast.success(t("clientConnection.paired", { name: result.serverName }));
          await load();
          await reloadContext();
        } else {
          toast.error(t(`clientConnection.pairing.${ClientPairingOutcome[result.outcome]}`));
        }
      } finally {
        claiming.current = false;
      }
    }, ClaimPollInterval);

    return () => clearInterval(timer);
  }, [ticket, load, reloadContext, t]);

  if (!isPureClient) {
    return (
      <div className="p-4 text-sm text-foreground-400">{t("clientConnection.onlyInClient")}</div>
    );
  }

  // The desktop app's console answers every connection route here with "managed by the
  // host": which servers this window can show is decided on the device's own Devices page.
  if (isConsole) {
    return (
      <div className="flex flex-col items-start gap-3 p-4 text-sm">
        <p className="text-foreground-500">{t("clientConnection.managedByDesktop")}</p>
        <Button
          size="sm"
          variant="flat"
          onPress={() =>
            openLocalView(devicesRoute("servers")).catch(() =>
              toast.error(t("federation.switcher.openFailed")),
            )
          }
        >
          {t("federation.switcher.manageDevices")}
        </Button>
      </div>
    );
  }

  /** Handshake first, so a wrong address is named before anyone types a code. */
  const connect = async () => {
    setBusy(true);
    try {
      const result = await clientApi.connect(address);

      if (result.outcome !== ServerHandshakeOutcome.Ok) {
        toast.error(
          t(`clientConnection.handshake.${ServerHandshakeOutcome[result.outcome]}`, {
            detail: result.detail ?? "",
          }),
        );

        return;
      }

      askHowToPair(result.server!.name);
    } finally {
      setBusy(false);
    }
  };

  const askHowToPair = (serverName: string) => {
    createPortal(Modal, {
      size: "md",
      title: t("clientConnection.pair.title", { name: serverName }),
      children: (
        <div className="flex flex-col gap-3">
          <p className="text-sm">{t("clientConnection.pair.description")}</p>
          <div className="flex gap-2">
            <Button color="primary" onPress={() => pairWithCode()}>
              {t("clientConnection.pair.useCode")}
            </Button>
            <Button variant="flat" onPress={() => requestApproval()}>
              {t("clientConnection.pair.askApproval")}
            </Button>
          </div>
        </div>
      ),
      defaultVisible: true,
      footer: { actions: ["cancel"] },
    });
  };

  const pairWithCode = () => {
    let code = "";

    createPortal(Modal, {
      size: "sm",
      title: t("clientConnection.pair.codeTitle"),
      children: (
        <Input
          label={t("clientConnection.pair.codeLabel")}
          onValueChange={(v) => {
            code = v;
          }}
        />
      ),
      defaultVisible: true,
      footer: { actions: ["ok", "cancel"] },
      onOk: async () => {
        const result = await clientApi.pairWithCode(address, code);

        if (result.outcome === ClientPairingOutcome.Paired) {
          toast.success(t("clientConnection.paired", { name: result.serverName }));
          await load();
          await reloadContext();
        } else {
          toast.error(t(`clientConnection.pairing.${ClientPairingOutcome[result.outcome]}`));
        }
      },
    });
  };

  const requestApproval = async () => {
    const result = await clientApi.requestPairing(address);

    if (!result.requestId) {
      toast.error(t(`clientConnection.pairing.${ClientPairingOutcome[result.outcome]}`));

      return;
    }

    setTicket({ ...result, address });
  };

  const forget = (serverId: string, name?: string) => {
    createPortal(Modal, {
      size: "md",
      title: t("clientConnection.forget.title"),
      children: t("clientConnection.forget.description", { name: name ?? serverId }),
      defaultVisible: true,
      footer: { actions: ["ok", "cancel"] },
      onOk: async () => {
        await clientApi.forgetServer(serverId);
        await load();
        await reloadContext();
      },
    });
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      <MigrationNotice />
      <div className="flex items-end gap-2">
        <Input
          className="max-w-[420px]"
          description={t("clientConnection.address.tip")}
          label={t("clientConnection.address.label")}
          placeholder="192.168.1.5:34567"
          value={address}
          onValueChange={setAddress}
        />
        <Button color="primary" isDisabled={!address || busy} isLoading={busy} onPress={connect}>
          {t("clientConnection.address.connect")}
        </Button>
      </div>

      {ticket?.requestId && (
        <div className="flex items-center gap-2 text-sm">
          <Snippet hideSymbol size="sm" variant="bordered">
            {ticket.requestId}
          </Snippet>
          <span className="text-foreground-400">{t("clientConnection.pair.waiting")}</span>
          <Button size="sm" variant="light" onPress={() => setTicket(undefined)}>
            {t("clientConnection.pair.stopWaiting")}
          </Button>
        </div>
      )}

      {/* Only once there is a server to talk to: before that the call is answered by
          nobody, and an update notice about a machine this client has not reached yet
          would be about nothing. */}
      {status?.serverReachable && status.activeServerId && (
        <ServerUpdateNotice
          serverName={status.servers.find((s) => s.isActive)?.serverName ?? undefined}
        />
      )}

      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">{t("clientConnection.servers.label")}</span>
          {status && !status.serverReachable && status.activeServerId && (
            <Chip color="warning" size="sm" variant="flat">
              {t("clientConnection.servers.unreachable")}
            </Chip>
          )}
        </div>

        {status?.servers.length ? (
          status.servers.map((s) => (
            <div key={s.serverId} className="flex items-center gap-2">
              <div className="flex flex-col">
                <span className="text-sm">{s.serverName ?? s.serverId}</span>
                <span className="text-xs font-mono text-foreground-400">{s.baseAddress}</span>
              </div>
              {s.isActive ? (
                <Chip color="success" size="sm" variant="flat">
                  {t("clientConnection.servers.active")}
                </Chip>
              ) : (
                <Button
                  size="sm"
                  variant="flat"
                  onPress={async () => {
                    await clientApi.activateServer(s.serverId);
                    await load();
                    await reloadContext();
                  }}
                >
                  {t("clientConnection.servers.use")}
                </Button>
              )}
              <Button
                color="danger"
                size="sm"
                variant="light"
                onPress={() => forget(s.serverId, s.serverName)}
              >
                {t("clientConnection.servers.forget")}
              </Button>
            </div>
          ))
        ) : (
          <span className="text-sm text-foreground-400">{t("clientConnection.servers.none")}</span>
        )}
      </div>

      {status && (
        <div className="text-xs text-foreground-400">
          {t("clientConnection.thisDevice", {
            name: status.deviceName,
            version: status.clientVersion,
          })}
        </div>
      )}
    </div>
  );
};

ClientConnectionPage.displayName = "ClientConnectionPage";

export default ClientConnectionPage;
