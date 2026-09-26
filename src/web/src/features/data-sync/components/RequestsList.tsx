import type { DataSyncAccessRequestView, DataSyncMapOutgoing } from "../api";
import type { DataSyncActions } from "../hooks/useDataSyncActions";
import type { SyncOutcome } from "../viewModels";
import type { RemoteAccessMode } from "@/sdk/constants";

import { forwardRef } from "react";
import { useTranslation } from "react-i18next";

import DataSyncOutgoingCard from "../map/DataSyncOutgoingCard";
import DataSyncRequestCard from "../map/DataSyncRequestCard";
import { isPendingRequest } from "../viewModels";

import { DataSyncErrorNotice, panelClass, SectionHeading } from "./common";

import { DataSyncRequestDirection } from "@/sdk/constants";

/*
 * Requests, both ways: devices asking to read this device's definitions — each with where it
 * really came from and "only approve your own devices" — and this device's own requests,
 * waiting with [Cancel] or ended with [Dismiss].
 */

interface Outgoing {
  key: string;
  nodeName: string;
  address?: string | null;
  outcome: SyncOutcome;
  expiresAt?: string | null;
  linkId?: number;
  requestId?: string;
}

const asOutcome = (value?: string | null): SyncOutcome =>
  value === "rejected" || value === "expired" ? value : "awaitingApproval";

/**
 * This device's own requests: those the listing says wait, and those the map view says ended.
 * One the other device approved is no request any more: its link says how it goes.
 */
export const outgoingRequests = (
  requests: DataSyncAccessRequestView[],
  ended: DataSyncMapOutgoing[],
  now: number = Date.now(),
): Outgoing[] => {
  const byNode = new Map<string, Outgoing>();

  for (const request of requests) {
    if (request.direction !== DataSyncRequestDirection.Outgoing || !isPendingRequest(request, now))
      continue;
    byNode.set(request.nodeId, {
      key: request.requestId,
      nodeName: request.nodeName,
      address: request.remoteAddress,
      outcome: "awaitingApproval",
      expiresAt: request.expiresAt,
      requestId: request.requestId,
    });
  }
  for (const outgoing of ended) {
    const known = byNode.get(outgoing.nodeId);

    byNode.set(outgoing.nodeId, {
      key: known?.key ?? `link-${outgoing.linkId}`,
      nodeName: outgoing.nodeName,
      address: outgoing.address ?? known?.address,
      outcome: asOutcome(outgoing.outcome),
      expiresAt: outgoing.expiresAt ?? known?.expiresAt,
      linkId: outgoing.linkId,
      requestId: known?.requestId,
    });
  }

  return Array.from(byNode.values());
};

const RequestsList = forwardRef<
  HTMLElement,
  {
    requests: DataSyncAccessRequestView[];
    outgoing: DataSyncMapOutgoing[];
    error?: Error;
    actions: DataSyncActions;
    canManage: boolean;
    sharingEnabled: boolean;
    remoteAccessMode: RemoteAccessMode;
    onRetry: () => void;
    now?: number;
  }
>(function RequestsList(
  { requests, outgoing, error, actions, canManage, sharingEnabled, remoteAccessMode, onRetry, now },
  ref,
) {
  const { t } = useTranslation();
  // Only what still asks for an answer: the listing keeps requests already decided too.
  const incoming = requests.filter(
    (request) =>
      request.direction === DataSyncRequestDirection.Incoming && isPendingRequest(request, now),
  );
  const own = outgoingRequests(requests, outgoing, now);

  if (!incoming.length && !own.length && !error) return null;

  return (
    <section
      ref={ref}
      aria-labelledby="data-sync-requests"
      className={`${panelClass} space-y-3`}
      data-testid="data-sync-requests"
      tabIndex={-1}
    >
      <SectionHeading id="data-sync-requests" title={t("dataSync.requests.title")} />
      <DataSyncErrorNotice error={error} onRetry={onRetry} />
      {incoming.length > 0 && (
        <div className="grid gap-3 lg:grid-cols-2">
          {incoming.map((request) => (
            <DataSyncRequestCard
              key={request.requestId}
              actions={actions}
              canManage={canManage}
              now={now}
              remoteAccessMode={remoteAccessMode}
              request={request}
              sharingEnabled={sharingEnabled}
            />
          ))}
        </div>
      )}
      {own.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-sm font-semibold">{t("dataSync.requests.own")}</h3>
          <div className="grid gap-3 lg:grid-cols-2">
            {own.map((item) => (
              <DataSyncOutgoingCard
                key={item.key}
                actions={actions}
                address={item.address}
                expiresAt={item.expiresAt}
                linkId={item.linkId}
                nodeName={item.nodeName}
                now={now}
                outcome={item.outcome}
                requestId={item.requestId}
              />
            ))}
          </div>
        </div>
      )}
    </section>
  );
});

export default RequestsList;
