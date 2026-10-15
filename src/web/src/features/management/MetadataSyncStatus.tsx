import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineSync } from "react-icons/ai";

import { setMetadataSyncPaused, type ManagementSource } from "./api";
import { canReadSourceMetadata } from "./metadata";
import { deviceColor } from "./resourceIdentity";
import { useManagementSources } from "./useManagementSources";

import { Button } from "@/components/bakaui";

export function MetadataSyncStatus({
  sources,
  onRefresh,
}: {
  sources: ManagementSource[];
  onRefresh: () => void;
}) {
  const { t, i18n } = useTranslation();
  const [busy, setBusy] = useState<string>();
  const [error, setError] = useState<string>();
  const remote = sources.filter((source) => !source.isLocal && source.metadata);

  if (!remote.length) return null;
  const pending = remote.filter((source) => !canReadSourceMetadata(source));
  const attention = remote.some(
    (source) =>
      !source.available ||
      source.metadata!.sync.error ||
      source.metadata!.sync.paused ||
      source.metadata!.sync.refreshPending ||
      source.metadata!.data.checkpoint?.cursor,
  );

  return (
    <details className="shrink-0 rounded-lg border border-divider bg-content1 px-3 py-2 text-xs">
      <summary className="cursor-pointer select-none text-default-600">
        {t("management.metadata.title")}
        <span className="ml-2 text-default-500">
          {t(
            pending.length
              ? "management.metadata.pendingSummary"
              : attention
                ? "management.metadata.attentionSummary"
                : "management.metadata.readySummary",
            {
              count: pending.length || remote.length,
            },
          )}
        </span>
      </summary>
      <div className="mt-2 flex flex-col divide-y divide-divider">
        {remote.map((source) => {
          const metadata = source.metadata!;
          const readable = canReadSourceMetadata(source);
          const initial = !metadata.data.activeGeneration;
          const updateRequired =
            metadata.sync.errorCode === "ManagementUpdateRequired" ||
            metadata.sync.errorCode === "MetadataContractChanged";
          const status = metadata.denied
            ? "denied"
            : updateRequired
              ? "updateRequired"
              : metadata.sync.paused
                ? "paused"
                : metadata.sync.isOffline || !source.available
                  ? "offline"
                  : metadata.sync.error
                    ? "failed"
                    : initial
                      ? "initial"
                      : metadata.sync.refreshPending ||
                          metadata.data.checkpoint?.complete === false ||
                          metadata.data.checkpoint?.cursor
                        ? "updating"
                        : "ready";
          const lastSync = metadata.sync.lastSyncedAtUtc
            ? new Date(metadata.sync.lastSyncedAtUtc)
            : undefined;
          const syncing = ["initial", "updating"].includes(status);

          return (
            <div key={source.serverId} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
              <span
                aria-hidden
                className="h-2 w-2 shrink-0 rounded-full"
                style={{ backgroundColor: deviceColor(source.serverId) }}
              />
              <span className="max-w-48 truncate font-medium" title={source.name}>
                {source.name}
              </span>
              <span className="flex items-center gap-1 text-default-500">
                {syncing && <AiOutlineSync aria-hidden className="animate-spin" />}
                {t(`management.metadata.${status}`)}
              </span>
              <span className="tabular-nums text-default-500">
                {t(
                  initial ? "management.metadata.stagedCount" : "management.metadata.resourceCount",
                  {
                    count: initial
                      ? metadata.data.stagedResourceCount
                      : metadata.data.resourceCount,
                  },
                )}
              </span>
              {lastSync && !Number.isNaN(lastSync.getTime()) && (
                <span className="text-default-400" title={lastSync.toLocaleString(i18n.language)}>
                  {t("management.metadata.lastSync", {
                    time: lastSync.toLocaleString(i18n.language),
                  })}
                </span>
              )}
              <Button
                className="ml-auto h-6 min-w-0 px-2 text-xs"
                isDisabled={!!busy || metadata.denied || !source.libraryEpoch || updateRequired}
                isLoading={busy === source.serverId}
                size="sm"
                variant="light"
                onPress={async () => {
                  setBusy(source.serverId);
                  setError(undefined);
                  try {
                    await setMetadataSyncPaused(source, !metadata.sync.paused);
                    onRefresh();
                  } catch (cause) {
                    setError(cause instanceof Error ? cause.message : String(cause));
                  } finally {
                    setBusy(undefined);
                  }
                }}
              >
                {t(
                  metadata.sync.paused ? "management.metadata.resume" : "management.metadata.pause",
                )}
              </Button>
              {(!readable ||
                status === "offline" ||
                metadata.sync.error ||
                metadata.sync.refreshPending) && (
                <p className="w-full pl-5 text-default-500">
                  {t(
                    metadata.denied
                      ? "management.metadata.deniedHelp"
                      : updateRequired
                        ? "management.metadata.updateRequiredHelp"
                        : metadata.sync.refreshPending
                          ? "management.metadata.refreshPendingHelp"
                          : readable
                            ? "management.metadata.cachedHelp"
                            : "management.metadata.incompleteHelp",
                  )}
                  {metadata.sync.error && (
                    <span className="ml-1 break-words">{metadata.sync.error}</span>
                  )}
                </p>
              )}
            </div>
          );
        })}
      </div>
      {error && (
        <p className="mt-1 text-danger" role="alert">
          {error}
        </p>
      )}
    </details>
  );
}

/** Devices page uses the same local status feed; polling never probes remote devices. */
export function MetadataSyncStatusSection() {
  const { sources, refresh } = useManagementSources();

  return <MetadataSyncStatus sources={sources} onRefresh={refresh} />;
}
