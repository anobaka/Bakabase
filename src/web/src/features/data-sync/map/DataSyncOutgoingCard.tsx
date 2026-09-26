import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { SyncOutcome } from "../viewModels";

import { useTranslation } from "react-i18next";

import { dataSyncApi, DataSyncProblemError } from "../api";
import { minutesLeft } from "../times";
import { outgoingRequestIdOf } from "../viewModels";
import { smallButtonClass } from "../components/common";

import { DataSyncProblemCode } from "@/sdk/constants";

/*
 * This device's own request to read another device's definitions: waiting, with [Cancel], or
 * ended — rejected or expired — with [Dismiss]. Nothing the reader filed vanishes by itself
 * (`.claude/rules/federation.md`): an ended request stays until it is dismissed, which resets
 * the link it belonged to. [Cancel] only ever withdraws the request: the link, its sync state
 * and its pending decisions stay (resetting them is a choice of its own, confirmed apart).
 */

export interface DataSyncOutgoingCardProps {
  nodeName: string;
  address?: string | null;
  outcome: SyncOutcome;
  expiresAt?: string | null;
  /** The link the request belongs to: dismissing an ended request resets it. */
  linkId?: number;
  /** The request itself, while it waits: cancelling withdraws it. */
  requestId?: string;
  /**
   * The device the request went to, where the request's id is not at hand (the device map's
   * record carries none): [Cancel] finds this device's own request to it and withdraws that.
   */
  nodeId?: string;
  actions: DataSyncPanelActions;
  now?: number;
}

export default function DataSyncOutgoingCard({
  nodeName,
  address,
  outcome,
  expiresAt,
  linkId,
  requestId,
  nodeId,
  actions,
  now,
}: DataSyncOutgoingCardProps) {
  const { t } = useTranslation();
  const name = nodeName;
  const waiting = outcome === "awaitingApproval";
  const minutes = minutesLeft(expiresAt, now);

  const cancel = () =>
    void actions.run(async () => {
      const id =
        requestId ??
        (nodeId ? outgoingRequestIdOf(await dataSyncApi.requests(), nodeId, now) : undefined);

      // Answered or withdrawn meanwhile: said so, and the listings read again show how it ended.
      if (!id) throw new DataSyncProblemError({ code: DataSyncProblemCode.RequestNotFound });
      await dataSyncApi.cancelRequest(id);
    }, ["dataSync", "sharing"]);
  const dismiss = () =>
    linkId !== undefined &&
    void actions.run(() => dataSyncApi.resetLink(linkId), ["dataSync", "sharing"]);

  return (
    <article
      className={`space-y-2 rounded-lg border p-3 text-sm ${
        waiting ? "border-primary/30 bg-primary/5" : "border-default-200 bg-default-50"
      }`}
      data-outcome={outcome}
      data-testid="data-sync-outgoing-card"
    >
      <p className="font-medium">
        {waiting
          ? t("dataSync.status.AwaitingAccess", { name })
          : outcome === "rejected"
            ? t("dataSync.status.AccessRejected", { name })
            : t("dataSync.outgoing.expired", { name })}
      </p>
      {address && <p className="break-all text-xs text-default-500">{address}</p>}
      {waiting && (
        <p className="text-xs text-default-500">
          {t("dataSync.link.approveThere", { name })}
          {minutes > 0 ? ` ${t("dataSync.request.expiresIn", { count: minutes })}` : ""}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {waiting
          ? (requestId || nodeId) && (
              <button
                className={smallButtonClass}
                data-testid="data-sync-outgoing-cancel"
                disabled={actions.busy}
                type="button"
                onClick={cancel}
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
                onClick={dismiss}
              >
                {t("dataSync.link.dismiss")}
              </button>
            )}
      </div>
    </article>
  );
}
