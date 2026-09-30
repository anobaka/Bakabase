import type { DataSyncMapRequest } from "../api";
import type { DataSyncActions } from "../hooks/useDataSyncActions";
import type { RemoteAccessMode } from "@/sdk/constants";

import { forwardRef } from "react";
import { useTranslation } from "react-i18next";

import DataSyncRequestCard from "../map/DataSyncRequestCard";
import { hasPassed } from "../times";

import { panelClass, SectionHeading } from "./common";

/*
 * Devices asking to read this device's definitions — each with where it really came from and
 * "only approve your own devices". This device's own requests show on the device they went to.
 */

const RequestsList = forwardRef<
  HTMLElement,
  {
    requests: DataSyncMapRequest[];
    actions: DataSyncActions;
    canManage: boolean;
    sharingEnabled: boolean;
    remoteAccessMode: RemoteAccessMode;
    now?: number;
  }
>(function RequestsList(
  { requests, actions, canManage, sharingEnabled, remoteAccessMode, now },
  ref,
) {
  const { t } = useTranslation();
  // Only what still asks for an answer: one may run out between two reads.
  const incoming = requests.filter((request) => !hasPassed(request.expiresAt, now));

  if (!incoming.length) return null;

  return (
    <section
      ref={ref}
      aria-labelledby="data-sync-requests"
      className={`${panelClass} space-y-3`}
      data-testid="data-sync-requests"
      tabIndex={-1}
    >
      <SectionHeading id="data-sync-requests" title={t("dataSync.requests.title")} />
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
    </section>
  );
});

export default RequestsList;
