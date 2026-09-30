import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { SyncPeer } from "../viewModels";

import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { minutesLeft } from "../times";
import { smallButtonClass } from "../components/common";

/*
 * This device's own request to read another device's definitions: waiting, with [Cancel], or
 * ended — rejected or expired — with [Dismiss]. Nothing the reader filed vanishes by itself
 * (`.claude/rules/federation.md`): an ended request stays until it is dismissed, which resets
 * the link it belonged to. [Cancel] only ever withdraws the request: the link, its sync state
 * and its pending decisions stay (resetting them is a choice of its own, confirmed apart).
 */

export default function DataSyncOutgoingCard({
  peer,
  actions,
  now,
}: {
  /** The device, with its request (`outcome`, `requestId`) and the link it belongs to. */
  peer: SyncPeer;
  actions: DataSyncPanelActions;
  now?: number;
}) {
  const { t } = useTranslation();
  const { name, linkId, requestId } = peer;
  const waiting = peer.outcome === "awaitingApproval";
  const minutes = minutesLeft(peer.outcomeExpiresAt, now);

  return (
    <article
      className={`space-y-2 rounded-lg border p-3 text-sm ${
        waiting ? "border-primary/30 bg-primary/5" : "border-default-200 bg-default-50"
      }`}
      data-outcome={peer.outcome}
      data-testid="data-sync-outgoing-card"
    >
      <p className="font-medium">
        {waiting
          ? t("dataSync.status.AwaitingAccess", { name })
          : peer.outcome === "rejected"
            ? t("dataSync.status.AccessRejected", { name })
            : t("dataSync.outgoing.expired", { name })}
      </p>
      {peer.address && <p className="break-all text-xs text-default-500">{peer.address}</p>}
      {waiting && (
        <p className="text-xs text-default-500">
          {t("dataSync.link.approveThere", { name })}
          {minutes > 0 ? ` ${t("dataSync.request.expiresIn", { count: minutes })}` : ""}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {waiting
          ? requestId && (
              <button
                className={smallButtonClass}
                data-testid="data-sync-outgoing-cancel"
                disabled={actions.busy}
                type="button"
                onClick={() =>
                  void actions.run(
                    () => dataSyncApi.cancelRequest(requestId),
                    ["dataSync", "sharing"],
                  )
                }
              >
                {t("dataSync.outgoing.cancel")}
              </button>
            )
          : linkId !== undefined && (
              <button
                className={smallButtonClass}
                data-testid="data-sync-outgoing-dismiss"
                disabled={actions.busy}
                type="button"
                onClick={() =>
                  void actions.run(() => dataSyncApi.resetLink(linkId), ["dataSync", "sharing"])
                }
              >
                {t("dataSync.link.dismiss")}
              </button>
            )}
      </div>
    </article>
  );
}
