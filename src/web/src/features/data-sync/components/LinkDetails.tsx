import type { Ref } from "react";
import type { DataSyncActions } from "../hooks/useDataSyncActions";
import type { SyncPeer } from "../viewModels";
import type { RemoteAccessMode } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import { dataSyncApi } from "../api";
import DataSyncOutgoingCard from "../map/DataSyncOutgoingCard";

import { DataSyncErrorNotice, linkButtonClass, smallButtonClass, syncText } from "./common";
import SyncRuleDrawing from "./SyncRuleDrawing";

import { DataSyncLinkMode, DataSyncLinkState } from "@/sdk/constants";
import { DismissButton } from "@/features/federation/components/common";

/*
 * The details of one device, beside the diagram: the rule editor, then what the link keeps
 * apart — definitions skipped, withheld until this device is updated, no longer offered there —
 * and the ways to start over.
 */

export interface LinkDetailsProps {
  peer: SyncPeer;
  /** This device's own request to it, while it waits: cancelling withdraws it. */
  outgoingRequestId?: string;
  actions: DataSyncActions;
  canManage: boolean;
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode;
  selfName: string;
  headingRef: Ref<HTMLHeadingElement>;
  closable: boolean;
  onClose: () => void;
  onCreateCode: () => void;
  /** Opens the list of definitions and how each syncs, on those not synced whole. */
  onShowDefinitions: () => void;
  /** Dismisses what the last action said, the keyboard going to the heading. */
  onDismissMessage: (clear: () => void) => () => void;
  now?: number;
}

export default function LinkDetails({
  peer,
  outgoingRequestId,
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
  selfName,
  headingRef,
  closable,
  onClose,
  onCreateCode,
  onShowDefinitions,
  onDismissMessage,
  now,
}: LinkDetailsProps) {
  const { t } = useTranslation();
  const name = peer.name;
  const ended = peer.outcome === "rejected" || peer.outcome === "expired";
  const linkId = peer.linkId;
  const reads =
    linkId !== undefined &&
    peer.mode === DataSyncLinkMode.Off &&
    peer.state === DataSyncLinkState.Stopped &&
    !ended;

  const reset = () =>
    linkId !== undefined &&
    actions.confirm({
      title: t("dataSync.link.reset.title", { name }),
      description: t("dataSync.link.reset.description", { name }),
      action: () => dataSyncApi.resetLink(linkId),
      refresh: ["dataSync"],
    });
  const stopReading = () =>
    actions.confirm({
      title: t("dataSync.link.stopReading.title", { name }),
      description: t("dataSync.link.stopReading.description", { name }),
      action: () => dataSyncApi.forgetAccess(peer.nodeId),
      refresh: ["dataSync"],
    });

  return (
    <section
      aria-labelledby="data-sync-details-title"
      className="space-y-4"
      data-peer={peer.nodeId}
      data-testid="data-sync-link-details"
    >
      <header className="flex items-start gap-3">
        <div className="min-w-0 flex-1">
          <p className={`text-xs font-medium ${syncText}`}>{t("dataSync.title")}</p>
          <h2
            ref={headingRef}
            className="break-words text-lg font-semibold outline-none"
            id="data-sync-details-title"
            tabIndex={-1}
          >
            {name}
          </h2>
          {(peer.address || peer.peerAppVersion) && (
            <p className="mt-0.5 break-all text-xs text-default-500">
              {peer.address}
              {peer.peerAppVersion ? `${peer.address ? " · " : ""}v${peer.peerAppVersion}` : ""}
            </p>
          )}
        </div>
        {closable && (
          <button
            aria-label={t("dataSync.close")}
            className="-m-1 rounded p-1 text-default-500 hover:bg-default-100"
            data-testid="data-sync-details-close"
            type="button"
            onClick={onClose}
          >
            <AiOutlineClose aria-hidden />
          </button>
        )}
      </header>

      {(actions.error || actions.notice) && (
        <div className="space-y-2">
          <DataSyncErrorNotice
            error={actions.error}
            onDismiss={onDismissMessage(() => actions.setError(undefined))}
          />
          {actions.notice && (
            <div
              className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
              role="status"
            >
              <p>{actions.notice}</p>
              <DismissButton onClick={onDismissMessage(() => actions.setNotice(undefined))} />
            </div>
          )}
        </div>
      )}

      {ended ? (
        <DataSyncOutgoingCard
          actions={actions}
          address={peer.address}
          expiresAt={peer.outcomeExpiresAt}
          linkId={linkId}
          nodeName={name}
          now={now}
          outcome={peer.outcome!}
        />
      ) : (
        <SyncRuleDrawing
          actions={actions}
          canManage={canManage}
          now={now}
          peer={peer}
          remoteAccessMode={remoteAccessMode}
          selfName={selfName}
          sharingEnabled={sharingEnabled}
          onCreateCode={onCreateCode}
        />
      )}

      {!ended && linkId !== undefined && (
        <div className="space-y-2 text-sm" data-testid="data-sync-link-facts">
          {peer.excludedCount > 0 && (
            <p className="flex flex-wrap items-center gap-2">
              <span>{t("dataSync.link.skipped", { count: peer.excludedCount })}</span>
              <button className={linkButtonClass} type="button" onClick={onShowDefinitions}>
                {t("dataSync.link.show")}
              </button>
            </p>
          )}
          {peer.heldCount > 0 && <p>{t("dataSync.link.withheld", { count: peer.heldCount })}</p>}
          {peer.missingAtPeerCount > 0 && (
            <p className="text-default-500">
              {t("dataSync.link.missingAtPeer", { count: peer.missingAtPeerCount, name })}
            </p>
          )}
          <div className="flex flex-wrap gap-2 pt-1">
            {outgoingRequestId && peer.outcome === "awaitingApproval" && (
              <button
                className={smallButtonClass}
                disabled={actions.busy}
                type="button"
                onClick={() =>
                  void actions.run(
                    () => dataSyncApi.cancelRequest(outgoingRequestId),
                    ["dataSync", "sharing"],
                  )
                }
              >
                {t("dataSync.outgoing.cancel")}
              </button>
            )}
            {reads && (
              <button
                className={smallButtonClass}
                data-testid="data-sync-stop-reading"
                disabled={actions.busy}
                type="button"
                onClick={stopReading}
              >
                {t("dataSync.link.stopReading.button", { name })}
              </button>
            )}
            <button
              className={smallButtonClass}
              data-testid="data-sync-reset"
              disabled={actions.busy}
              type="button"
              onClick={reset}
            >
              {t("dataSync.link.reset.button")}
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
