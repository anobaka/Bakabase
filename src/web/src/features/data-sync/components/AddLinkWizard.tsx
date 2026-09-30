import type { DataSyncLinkResult, DataSyncPeerCandidate } from "../api";
import type { CandidateStatus, SyncPeer } from "../viewModels";
import type { RemoteAccessMode } from "@/sdk/constants";

import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineLoading3Quarters } from "react-icons/ai";

import { dataSyncApi } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { candidateStatus, isPickable, newSyncPeer } from "../viewModels";

import { buttonClass, DataSyncErrorNotice, fieldClass, primaryClass } from "./common";
import DataSyncDialog from "./DataSyncDialog";
import InlineConfirmation from "./InlineConfirmation";
import SyncRuleDrawing from "./SyncRuleDrawing";

import { DataSyncLinkState } from "@/sdk/constants";

/*
 * "Sync with another device" (spec §11.2): choose the device, then the rule editor for it — the
 * same one the page and the map show. A device this window cannot read yet is asked for access,
 * which only this device's own window or a device paired with it may do (§7.1.5); anywhere else
 * such devices are shown but cannot be picked, and the way in by address and code is not offered.
 * Once a link or copy is made the wizard closes, and the page shows the device and says what came
 * of it.
 */

/** What the page is told once a link or copy is made. */
export interface AddLinkCreated {
  nodeId?: string;
  notice: string;
}

/**
 * A device the list offers, as the rule editor shows it: by its id, with what it may read and
 * this device may read already — and, where a request goes out, the address it was listed at. A
 * device found only nearby is known to the server by nothing else (the same rule as the map's).
 */
const peerOfCandidate = (candidate: DataSyncPeerCandidate): SyncPeer => ({
  ...newSyncPeer(
    candidate.nodeId,
    candidate.name,
    candidate.weMayRead ? undefined : (candidate.address ?? undefined),
  ),
  weMayRead: candidate.weMayRead,
  peerMayRead: candidate.theyMayRead,
});

export default function AddLinkWizard({
  canManage,
  sharingEnabled,
  remoteAccessMode,
  selfName,
  onClose,
}: {
  canManage: boolean;
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode;
  selfName: string;
  /** Closes the wizard; with what was made, so the page can show the device and say so. */
  onClose: (created?: AddLinkCreated) => void;
}) {
  const { t } = useTranslation();
  const id = useId();
  const actions = useDataSyncActions(() => undefined);
  const [candidates, setCandidates] = useState<DataSyncPeerCandidate[]>();
  const [discovering, setDiscovering] = useState(true);
  const [loadError, setLoadError] = useState<Error>();
  const [chosen, setChosen] = useState<DataSyncPeerCandidate>();
  const [byAddress, setByAddress] = useState(false);
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  // The device the rule editor is shown for: the second step.
  const [peer, setPeer] = useState<SyncPeer>();

  // The devices already known at once, then — a few seconds later — those found nearby.
  useEffect(() => {
    let alive = true;

    dataSyncApi
      .peers(false)
      .then((known) => alive && setCandidates((current) => current ?? known))
      .catch(() => undefined);
    dataSyncApi
      .peers(true)
      .then((found) => alive && setCandidates(found))
      .catch(
        (cause) => alive && setLoadError(cause instanceof Error ? cause : new Error(String(cause))),
      )
      .finally(() => alive && setDiscovering(false));

    return () => {
      alive = false;
    };
  }, []);

  const addressReady = address.trim().length > 0 && /^\d{8}$/.test(code.trim());

  const created = ({ link, requestId }: DataSyncLinkResult) => {
    const name = link?.peerName ?? peer?.name ?? "";

    onClose({
      nodeId: link?.peerNodeId ?? (peer?.nodeId || undefined),
      notice: t(
        link?.state === DataSyncLinkState.AwaitingAccess || (!link && requestId)
          ? "dataSync.wizard.requested"
          : link?.state === DataSyncLinkState.AwaitingReview
            ? "dataSync.wizard.ready"
            : "dataSync.wizard.linked",
        { name },
      ),
    });
  };

  const statusText = (status: CandidateStatus, item: DataSyncPeerCandidate) =>
    t(`dataSync.wizard.candidate.${status}`, { name: item.name });

  const choose = (
    <div className="space-y-3" data-testid="data-sync-wizard-choose">
      <p className="text-sm">{t("dataSync.wizard.chooseIntro")}</p>
      <DataSyncErrorNotice error={loadError} />
      {discovering && (
        <p className="flex items-center gap-2 text-xs text-default-500" role="status">
          <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />
          {t("dataSync.wizard.discovering")}
        </p>
      )}
      {candidates && candidates.length === 0 && !discovering && (
        <p className="text-sm text-default-500">{t("dataSync.wizard.noneFound")}</p>
      )}
      {/* A plain list of buttons, the one chosen pressed: each is its own Tab stop. */}
      <ul aria-label={t("dataSync.wizard.devices")} className="space-y-1.5">
        {(candidates ?? []).map((item) => {
          const status = candidateStatus(item, canManage);
          const pickable = isPickable(status);
          const selected = chosen?.nodeId === item.nodeId;

          return (
            <li key={item.nodeId}>
              <button
                aria-disabled={pickable ? undefined : "true"}
                aria-pressed={selected}
                className={`w-full rounded-lg border p-2.5 text-left text-sm transition ${
                  selected ? "border-primary bg-primary/5" : "border-default-200"
                } ${pickable ? "hover:bg-default-100" : "cursor-not-allowed opacity-70"}`}
                data-candidate={item.nodeId}
                data-status={status}
                type="button"
                onClick={() => {
                  if (!pickable) return;
                  setByAddress(false);
                  setChosen(item);
                }}
              >
                <span className="block font-medium">{item.name}</span>
                <span className="block text-xs text-default-500">
                  {[item.address, statusText(status, item)].filter(Boolean).join(" · ")}
                </span>
              </button>
            </li>
          );
        })}
      </ul>
      {canManage && (
        <div className="space-y-2 rounded-lg border border-default-200 p-2.5">
          <label className="flex items-center gap-2 text-sm">
            <input
              checked={byAddress}
              data-testid="data-sync-wizard-by-address"
              type="checkbox"
              onChange={(event) => {
                setByAddress(event.target.checked);
                setChosen(undefined);
              }}
            />
            {t("dataSync.wizard.byAddress")}
          </label>
          {byAddress && (
            <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_10rem]">
              <label className="space-y-1 text-xs" htmlFor={`${id}-address`}>
                <span>{t("dataSync.wizard.address")}</span>
                <input
                  className={fieldClass}
                  id={`${id}-address`}
                  placeholder="192.168.1.10:34567"
                  value={address}
                  onChange={(event) => setAddress(event.target.value)}
                />
              </label>
              <label className="space-y-1 text-xs" htmlFor={`${id}-code`}>
                <span>{t("dataSync.wizard.code")}</span>
                <input
                  className={fieldClass}
                  id={`${id}-code`}
                  inputMode="numeric"
                  maxLength={8}
                  value={code}
                  onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
                />
              </label>
            </div>
          )}
        </div>
      )}
      <p className="text-xs text-default-500">{t("dataSync.wizard.notListed")}</p>
      {!canManage && <p className="text-xs text-default-500">{t("dataSync.manageElsewhere")}</p>}
    </div>
  );

  const footer = peer ? (
    <button
      className={buttonClass}
      disabled={actions.busy}
      type="button"
      onClick={() => {
        actions.reset();
        actions.cancelConfirmation();
        setPeer(undefined);
      }}
    >
      {t("dataSync.wizard.back")}
    </button>
  ) : (
    <>
      <button className={buttonClass} type="button" onClick={() => onClose()}>
        {t("dataSync.cancel")}
      </button>
      <button
        className={primaryClass}
        data-testid="data-sync-wizard-next"
        disabled={byAddress ? !addressReady : !chosen}
        type="button"
        onClick={() =>
          setPeer(
            byAddress
              ? { ...newSyncPeer("", address.trim(), address.trim()), code: code.trim() }
              : peerOfCandidate(chosen!),
          )
        }
      >
        {t("dataSync.wizard.next")}
      </button>
    </>
  );

  return (
    <DataSyncDialog
      busy={actions.busy}
      footer={footer}
      testId="data-sync-wizard"
      title={t("dataSync.wizard.open")}
      onClose={() => onClose()}
    >
      {peer ? (
        <div className="space-y-3" data-testid="data-sync-wizard-rule">
          <SyncRuleDrawing
            actions={actions}
            canManage={canManage}
            peer={peer}
            remoteAccessMode={remoteAccessMode}
            selfName={selfName}
            sharingEnabled={sharingEnabled}
            onCreated={created}
          />
          <DataSyncErrorNotice
            error={actions.error}
            onDismiss={() => actions.setError(undefined)}
          />
          {actions.confirmation && (
            <InlineConfirmation
              busy={actions.busy}
              confirmation={actions.confirmation}
              error={actions.confirmationError}
              onCancel={actions.cancelConfirmation}
              onConfirm={actions.confirmCurrent}
            />
          )}
        </div>
      ) : (
        choose
      )}
    </DataSyncDialog>
  );
}
