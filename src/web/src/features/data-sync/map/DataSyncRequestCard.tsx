import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { RemoteAccessMode } from "@/sdk/constants";

import { useId, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { minutesLeft } from "../times";
import {
  dataSyncKinds,
  failureReason,
  orderKinds,
  sharingNeeded,
  toggleKind,
  turnsOnWarning,
} from "../viewModels";
import { primaryClass, smallButtonClass } from "../components/common";

import { DataSyncRequestIntent } from "@/sdk/constants";

/*
 * One device asking to read this device's definitions — on the data sync page's Requests, and
 * on the device map for a node known only by its own request. Everything it says about itself
 * is a claim: the card shows where the request really came from, and warns when it claims to
 * be a device this one knows at another address (spec §7.2.5). Its approval options are
 * inline, before [Approve…], because a confirmation shows text only.
 */

/** What a card needs of a request: the map view's and the requests listing's records both fit. */
export interface DataSyncRequestLike {
  requestId: string;
  nodeId: string;
  nodeName: string;
  remoteAddress?: string | null;
  intent: DataSyncRequestIntent | number;
  expiresAt: string;
  claimsKnownDevice: boolean;
  knownAddress?: string | null;
}

export interface DataSyncRequestCardProps {
  request: DataSyncRequestLike;
  actions: DataSyncPanelActions;
  /** Whether this window may approve (spec §7.1.5); rejecting is open to every window. */
  canManage: boolean;
  /**
   * Sharing and remote access here: an approval can only be read while both are on, so
   * approving turns on whichever is off — remote access with pairing required — and says so.
   */
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode;
  now?: number;
}

export default function DataSyncRequestCard({
  request,
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
  now,
}: DataSyncRequestCardProps) {
  const { t } = useTranslation();
  const id = useId();
  const name = request.nodeName;
  const twoWay = request.intent === DataSyncRequestIntent.TwoWay;
  const [receiveBack, setReceiveBack] = useState(twoWay);
  const [kinds, setKinds] = useState<string[]>([...dataSyncKinds]);
  const from = request.remoteAddress || t("dataSync.request.unknownAddress");
  const text = t(twoWay ? "dataSync.request.twoWay" : "dataSync.request.follow", { name });
  const fromText = t("dataSync.request.from", { address: from });
  const claim = request.claimsKnownDevice
    ? t("dataSync.request.claim", {
        name,
        known: request.knownAddress || t("dataSync.request.unknownAddress"),
        from,
      })
    : undefined;
  const own = { sharingEnabled, remoteAccessMode };
  const minutes = minutesLeft(request.expiresAt, now);

  const approve = () =>
    actions.confirm({
      title: t("dataSync.request.approveTitle", { name }),
      description: text,
      warning: [claim ?? fromText, turnsOnWarning(t, own)].filter(Boolean).join(" "),
      action: async () => {
        // Remote access changes only where it is off (spec §7.2.4).
        if (sharingNeeded(own))
          await dataSyncApi.setSharing({ enabled: true, enablePairedRemoteAccess: true });
        const readBack = twoWay && receiveBack;
        const result = await dataSyncApi.approveRequest(request.requestId, {
          receiveBack: readBack,
          kinds: readBack ? orderKinds(kinds) : undefined,
        });

        // The card goes with the request: what the approval said stays with the host. Both ways
        // only once this device reads the other back; a read-back that failed says why (the link
        // made for it carries the reason, spec §7.2.4) and is tried again from its details.
        actions.setNotice(
          result.readBackGranted
            ? t("dataSync.request.approvedBothWays", { name })
            : readBack
              ? t("dataSync.request.approvedReadBackFailed", {
                  name,
                  reason: failureReason(t, result.createdLink?.lastErrorDetail ?? undefined),
                })
              : t("dataSync.request.approved", { name }),
        );
      },
      refresh: ["dataSync", "sharing"],
    });

  const reject = () =>
    void actions.run(() => dataSyncApi.rejectRequest(request.requestId), ["dataSync", "sharing"]);

  return (
    <article
      aria-labelledby={`${id}-title`}
      className="space-y-2 rounded-lg border border-warning/40 bg-warning/5 p-3 text-sm"
      data-request={request.requestId}
      data-testid="data-sync-request-card"
    >
      <p className="font-medium" id={`${id}-title`}>
        {text}
      </p>
      <p className="text-xs text-default-600" data-testid="data-sync-request-from">
        {fromText}
      </p>
      {claim && (
        <p
          className="rounded-md bg-warning/15 p-2 text-xs text-warning-700 dark:text-warning"
          data-testid="data-sync-request-claim"
        >
          {claim}
        </p>
      )}
      <p className="text-xs text-default-500">
        {minutes > 0
          ? t("dataSync.request.expiresIn", { count: minutes })
          : t("dataSync.request.expired")}
      </p>
      {canManage && twoWay && (
        <fieldset
          className="space-y-1.5 rounded-md bg-content1 p-2"
          data-testid="data-sync-request-options"
        >
          <legend className="sr-only">{t("dataSync.request.options")}</legend>
          <label className="flex items-start gap-2 text-xs">
            <input
              checked={receiveBack}
              className="mt-0.5 accent-secondary"
              data-testid="data-sync-request-receive-back"
              disabled={actions.busy}
              type="checkbox"
              onChange={(event) => setReceiveBack(event.target.checked)}
            />
            <span>{t("dataSync.request.receiveBack", { name })}</span>
          </label>
          {receiveBack && (
            <div className="flex flex-wrap items-center gap-3 pl-5 text-xs">
              {dataSyncKinds.map((kind) => {
                const on = kinds.includes(kind);

                return (
                  <label key={kind} className="inline-flex items-center gap-1.5">
                    <input
                      aria-disabled={on && kinds.length === 1 ? "true" : undefined}
                      checked={on}
                      className="accent-secondary"
                      data-kind={kind}
                      data-testid="data-sync-request-kind"
                      disabled={actions.busy}
                      type="checkbox"
                      onChange={() => {
                        const next = toggleKind(kinds, kind);

                        if (next) setKinds(next);
                      }}
                    />
                    {t(`dataSync.kind.${kind}`, { defaultValue: kind })}
                  </label>
                );
              })}
            </div>
          )}
        </fieldset>
      )}
      <div className="flex flex-wrap gap-2">
        {canManage && (
          <button
            className={`${primaryClass} !px-2.5 !py-1 !text-xs`}
            data-testid="data-sync-request-approve"
            disabled={actions.busy}
            type="button"
            onClick={approve}
          >
            {t("dataSync.request.approve")}
          </button>
        )}
        <button
          className={smallButtonClass}
          data-testid="data-sync-request-reject"
          disabled={actions.busy}
          type="button"
          onClick={reject}
        >
          {t("dataSync.request.reject")}
        </button>
      </div>
      {!canManage && <p className="text-xs text-default-500">{t("dataSync.manageElsewhere")}</p>}
    </article>
  );
}
