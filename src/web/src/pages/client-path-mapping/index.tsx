"use client";

import type { ClientPathMapping, ClientStatus } from "@/core/clientApi";
import type { ManagedPathMappingSnapshot } from "@/features/federation/components/ManagedServerPathMappings";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { clientApi } from "@/core/clientApi";
import ManagedServerPathMappings from "@/features/federation/components/ManagedServerPathMappings";
import { ErrorNotice } from "@/features/federation/components/common";
import {
  mappingTreeLoader,
  saveManagedPathMappings,
} from "@/features/federation/managedPathMappingTarget";
import { useIsPureClient } from "@/stores/remoteAccess";

/**
 * Where the active server's libraries are on this machine.
 *
 * Nothing can infer this — a container serving `/data/media` and a desktop mounting the
 * same share as `Z:\media` have no way to discover each other's names — so it is
 * configuration, and until a library is listed here the client refuses to open its files
 * rather than guessing at a location.
 */
const ClientPathMappingPage = () => {
  const { t } = useTranslation();
  const isPureClient = useIsPureClient();

  const [status, setStatus] = useState<ClientStatus>();
  const [error, setError] = useState<Error>();
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    try {
      const fresh = await clientApi.status();

      setStatus(fresh);
      setError(undefined);
    } catch (failure) {
      setError(failure instanceof Error ? failure : new Error(String(failure)));
    }
  }, []);

  useEffect(() => {
    if (isPureClient) {
      load();
    }
  }, [isPureClient, load]);

  if (!isPureClient) {
    return (
      <div className="p-4 text-sm text-foreground-400">{t("clientPathMapping.onlyInClient")}</div>
    );
  }

  const active = status?.servers.find((s) => s.isActive);

  if (!active) {
    return (
      <div className="p-4 text-sm text-foreground-400">
        <ErrorNotice error={error} onRetry={() => void load()} />
        {!error && t("clientPathMapping.noServer")}
      </div>
    );
  }

  const snapshot: ManagedPathMappingSnapshot = {
    serverId: active.serverId,
    name: active.serverName,
    pairedAt: active.pairedAt,
    pathMappings: active.pathMappings,
  };
  const save = async (rows: ClientPathMapping[], expectedMappings?: ClientPathMapping[]) => {
    if (!expectedMappings) return false;
    setSaving(true);
    try {
      await saveManagedPathMappings(
        { kind: "relay", serverId: active.serverId },
        rows,
        expectedMappings,
      );
      setStatus(
        (current) =>
          current && {
            ...current,
            servers: current.servers.map((server) =>
              server.serverId === active.serverId ? { ...server, pathMappings: rows } : server,
            ),
          },
      );
      toast.success(t("common.success.saved"));

      return true;
    } catch (failure) {
      await load();
      throw failure;
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">
          {t("clientPathMapping.title", { name: active.serverName ?? active.serverId })}
        </span>
        <span className="text-xs text-foreground-400">{t("clientPathMapping.description")}</span>
      </div>

      <ErrorNotice error={error} onRetry={() => void load()} />
      <p className="text-xs text-default-500">{t("federation.managedMappings.relayLocalPath")}</p>
      <ManagedServerPathMappings
        expanded
        busy={saving}
        chooseLocal={false}
        className=""
        loadTree={mappingTreeLoader({ kind: "relay", serverId: active.serverId })}
        server={snapshot}
        targetKind="relay"
        onSave={save}
      />

      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">{t("clientPathMapping.canRunHere")}</span>
        <span className="text-xs text-foreground-400">{t("clientPathMapping.canRunHereTip")}</span>
        <div className="flex flex-col gap-0.5">
          {status?.implementedUserMachineRoutes.map((route) => (
            <span key={route} className="text-xs font-mono text-foreground-500">
              {route}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
};

ClientPathMappingPage.displayName = "ClientPathMappingPage";

export default ClientPathMappingPage;
