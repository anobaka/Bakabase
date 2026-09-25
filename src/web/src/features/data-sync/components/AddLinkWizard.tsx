import type { DataSyncPeerCandidate } from "../api";
import type { DataSyncActions } from "../hooks/useDataSyncActions";
import type { CandidateStatus } from "../viewModels";

import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineLoading3Quarters } from "react-icons/ai";

import { dataSyncApi } from "../api";
import { dataSyncReviewRoute } from "../routes";
import { candidateStatus, dataSyncKinds, isPickable, toggleKind } from "../viewModels";

import { buttonClass, DataSyncErrorNotice, fieldClass, primaryClass } from "./common";
import DataSyncDialog from "./DataSyncDialog";

import { DataSyncLinkMode, DataSyncLinkState, RemoteAccessMode } from "@/sdk/constants";

/*
 * "Sync with another device": choose the device, choose how, see what happens next (spec
 * §11.2). A device this window cannot read yet is asked for access, which only this device's
 * own window or a device paired with it may do (§7.1.5); anywhere else such devices are shown
 * but cannot be picked, and the way in by address and code is not offered.
 */

type Target =
  | { kind: "candidate"; candidate: DataSyncPeerCandidate }
  | { kind: "address"; address: string; code: string };

type How = "follow" | "twoWay" | "copyOnce";

type Outcome =
  | { kind: "requested"; name: string; nodeId?: string }
  | { kind: "ready"; name: string; linkId: number; nodeId?: string }
  | { kind: "fetching"; name: string; linkId: number; nodeId?: string }
  | { kind: "linked"; name: string; nodeId?: string };

export interface AddLinkWizardProps {
  actions: DataSyncActions;
  canManage: boolean;
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode;
  selfName: string;
  /** Closes the wizard; with the device a link was made to, so the page can show it. */
  onClose: (nodeId?: string) => void;
}

export default function AddLinkWizard({
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
  selfName,
  onClose,
}: AddLinkWizardProps) {
  const { t } = useTranslation();
  const id = useId();
  const [step, setStep] = useState<"choose" | "how" | "outcome">("choose");
  const [candidates, setCandidates] = useState<DataSyncPeerCandidate[]>();
  const [discovering, setDiscovering] = useState(true);
  const [loadError, setLoadError] = useState<Error>();
  const [target, setTarget] = useState<Target>();
  const [byAddress, setByAddress] = useState(false);
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [how, setHow] = useState<How>("twoWay");
  const [kinds, setKinds] = useState<string[]>([...dataSyncKinds]);
  const [error, setError] = useState<Error>();
  const [outcome, setOutcome] = useState<Outcome>();

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

  const candidate = target?.kind === "candidate" ? target.candidate : undefined;
  const targetName = candidate?.name ?? (target?.kind === "address" ? target.address : "");
  /** Whether going on sends a request: always by address, else when this device cannot read it. */
  const sendsRequest = target?.kind === "address" || !candidate?.weMayRead;
  const twoWayCreatesAccess = !candidate?.theyMayRead;
  const twoWayAllowed = canManage || !twoWayCreatesAccess;
  const turnOnSharing =
    how === "twoWay" &&
    twoWayCreatesAccess &&
    (!sharingEnabled || remoteAccessMode === RemoteAccessMode.Disabled);
  const addressReady = address.trim().length > 0 && /^\d{8}$/.test(code.trim());

  const start = () => {
    if (!target) return;
    const peer =
      target.kind === "candidate"
        ? { peerNodeId: target.candidate.nodeId }
        : { address: target.address, code: target.code };

    void actions.run(
      async () => {
        if (turnOnSharing)
          await dataSyncApi.setSharing({
            enabled: true,
            enablePairedRemoteAccess: remoteAccessMode === RemoteAccessMode.Disabled,
          });
        if (how === "copyOnce") {
          const review = await dataSyncApi.copyOnce({ ...peer, kinds });
          const linkId = review.linkId ?? undefined;

          setOutcome(
            linkId === undefined
              ? { kind: "requested", name: targetName, nodeId: candidate?.nodeId }
              : review.reviewId
                ? { kind: "ready", name: targetName, linkId, nodeId: candidate?.nodeId }
                : sendsRequest
                  ? { kind: "requested", name: targetName, nodeId: candidate?.nodeId }
                  : { kind: "fetching", name: targetName, linkId, nodeId: candidate?.nodeId },
          );
        } else {
          const result = await dataSyncApi.createLink({
            ...peer,
            mode: how === "twoWay" ? DataSyncLinkMode.TwoWay : DataSyncLinkMode.Follow,
            kinds,
          });
          const link = result.link;
          const name = link?.peerName ?? targetName;
          const nodeId = link?.peerNodeId ?? candidate?.nodeId;

          setOutcome(
            link?.state === DataSyncLinkState.AwaitingAccess || (!link && result.requestId)
              ? { kind: "requested", name, nodeId }
              : link && (link.state === DataSyncLinkState.AwaitingReview || result.reviewId)
                ? { kind: "ready", name, linkId: link.id, nodeId }
                : { kind: "linked", name, nodeId },
          );
        }
        setStep("outcome");
      },
      ["dataSync"],
      setError,
    );
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
      <ul aria-label={t("dataSync.wizard.devices")} className="space-y-1.5" role="listbox">
        {(candidates ?? []).map((item) => {
          const status = candidateStatus(item, canManage);
          const pickable = isPickable(status);
          const selected = candidate?.nodeId === item.nodeId;

          return (
            <li key={item.nodeId}>
              <button
                aria-disabled={pickable ? undefined : "true"}
                aria-selected={selected}
                className={`w-full rounded-lg border p-2.5 text-left text-sm transition ${
                  selected ? "border-primary bg-primary/5" : "border-default-200"
                } ${pickable ? "hover:bg-default-100" : "cursor-not-allowed opacity-70"}`}
                data-candidate={item.nodeId}
                data-status={status}
                role="option"
                type="button"
                onClick={() => {
                  if (!pickable) return;
                  setByAddress(false);
                  setTarget({ kind: "candidate", candidate: item });
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
                setTarget(undefined);
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

  const chooseHow = (
    <div className="space-y-3" data-testid="data-sync-wizard-how">
      <p className="text-sm font-medium">{t("dataSync.wizard.howWith", { name: targetName })}</p>
      <fieldset className="space-y-1.5">
        <legend className="sr-only">{t("dataSync.mode.legend", { name: targetName })}</legend>
        {(["follow", "twoWay", "copyOnce"] as const).map((item) => {
          const disabled = item === "twoWay" && !twoWayAllowed;
          const title =
            item === "copyOnce" ? t("dataSync.copyOnce.button") : t(`dataSync.mode.${item}`);

          return (
            <label
              key={item}
              className={`grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 rounded-lg border p-2.5 text-sm ${
                how === item ? "border-secondary bg-secondary/5" : "border-default-200"
              } ${disabled ? "opacity-60" : "cursor-pointer"}`}
            >
              <input
                checked={how === item}
                className="row-span-2 mt-0.5 accent-secondary"
                data-testid={`data-sync-wizard-how-${item}`}
                disabled={disabled}
                name={`${id}-how`}
                type="radio"
                onChange={() => setHow(item)}
              />
              <span className="font-medium">{title}</span>
              <span className="text-xs text-default-500">
                {t(`dataSync.wizard.explain.${item}`, { name: targetName })}
              </span>
            </label>
          );
        })}
      </fieldset>
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <span className="text-xs text-default-500">{t("dataSync.wizard.kinds")}</span>
        {dataSyncKinds.map((kind) => (
          <label key={kind} className="inline-flex items-center gap-1.5">
            <input
              aria-disabled={kinds.length === 1 && kinds.includes(kind) ? "true" : undefined}
              checked={kinds.includes(kind)}
              className="accent-secondary"
              type="checkbox"
              onChange={() => {
                const next = toggleKind(kinds, kind);

                if (next) setKinds(next);
              }}
            />
            {t(`dataSync.kind.${kind}`, { defaultValue: kind })}
          </label>
        ))}
      </div>
      {how === "twoWay" && (
        <p
          className="rounded-lg border border-warning/40 bg-warning/10 p-3 text-xs"
          data-testid="data-sync-wizard-consent"
        >
          {t("dataSync.twoWay.consent", { name: targetName })}
          {turnOnSharing && !sharingEnabled ? ` ${t("dataSync.twoWay.turnsOnSharing")}` : ""}
          {turnOnSharing && remoteAccessMode === RemoteAccessMode.Disabled
            ? ` ${t("dataSync.sharing.remoteAccess")}`
            : ""}
        </p>
      )}
      {how !== "twoWay" && sendsRequest && (
        <div className="space-y-1 rounded-lg bg-default-100 p-3 text-xs">
          <p>{t("dataSync.follow.confirm", { name: targetName })}</p>
          <p className="italic">{t("dataSync.request.follow", { name: selfName })}</p>
        </div>
      )}
      <DataSyncErrorNotice error={error} />
    </div>
  );

  const outcomeView = outcome && (
    <div
      className="space-y-3 text-sm"
      data-outcome={outcome.kind}
      data-testid="data-sync-wizard-outcome"
    >
      {outcome.kind === "requested" && (
        <p>{t("dataSync.wizard.requested", { name: outcome.name })}</p>
      )}
      {outcome.kind === "ready" && (
        <>
          <p>{t("dataSync.wizard.ready", { name: outcome.name })}</p>
          <Link
            className={primaryClass}
            to={dataSyncReviewRoute(outcome.linkId)}
            onClick={() => onClose(outcome.nodeId)}
          >
            {t("dataSync.link.review")}
          </Link>
        </>
      )}
      {outcome.kind === "fetching" && (
        <p>{t("dataSync.wizard.fetching", { name: outcome.name })}</p>
      )}
      {outcome.kind === "linked" && <p>{t("dataSync.wizard.linked", { name: outcome.name })}</p>}
    </div>
  );

  const footer =
    step === "choose" ? (
      <>
        <button className={buttonClass} type="button" onClick={() => onClose()}>
          {t("dataSync.cancel")}
        </button>
        <button
          className={primaryClass}
          data-testid="data-sync-wizard-next"
          disabled={byAddress ? !addressReady : !target}
          type="button"
          onClick={() => {
            if (byAddress)
              setTarget({ kind: "address", address: address.trim(), code: code.trim() });
            setHow(twoWayAllowedFor(byAddress, canManage, candidate) ? "twoWay" : "follow");
            setStep("how");
          }}
        >
          {t("dataSync.wizard.next")}
        </button>
      </>
    ) : step === "how" ? (
      <>
        <button
          className={buttonClass}
          disabled={actions.busy}
          type="button"
          onClick={() => {
            setError(undefined);
            setStep("choose");
          }}
        >
          {t("dataSync.wizard.back")}
        </button>
        <button
          className={primaryClass}
          data-testid="data-sync-wizard-start"
          disabled={actions.busy || (how === "twoWay" && !twoWayAllowed)}
          type="button"
          onClick={start}
        >
          {t(how === "copyOnce" ? "dataSync.wizard.startCopy" : "dataSync.wizard.start")}
        </button>
      </>
    ) : (
      <button className={primaryClass} type="button" onClick={() => onClose(outcome?.nodeId)}>
        {t("dataSync.done")}
      </button>
    );

  return (
    <DataSyncDialog
      busy={actions.busy}
      footer={footer}
      testId="data-sync-wizard"
      title={t("dataSync.wizard.open")}
      onClose={() => onClose(outcome?.nodeId)}
    >
      {step === "choose" ? choose : step === "how" ? chooseHow : outcomeView}
    </DataSyncDialog>
  );
}

/** Both ways is where the wizard starts, when this window may choose it for that device. */
const twoWayAllowedFor = (
  byAddress: boolean,
  canManage: boolean,
  candidate?: DataSyncPeerCandidate,
) => canManage || (!byAddress && !!candidate?.theyMayRead);
