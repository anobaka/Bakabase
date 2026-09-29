import type { ReactNode } from "react";
import type { PairingRequest, PairingResult, PathMapping, Peer } from "../types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlinePlus } from "react-icons/ai";

import { buttonClass, fieldClass, panelClass, primaryClass } from "../components/common";
import InviteBlock from "../components/InviteBlock";
import PeerPathMappings from "../components/PeerPathMappings";
import { useFocusOnOpen } from "../hooks/useFocusOnOpen";
import { revealClass } from "../hooks/useSectionReveal";
import { federationPeerApi } from "../peerApi";
import { devicesRoute } from "../switching";

import { useDevicesPage } from "./context";
import TabHeading from "./TabHeading";

import { RemoteAccessMode } from "@/sdk/constants";

const chip = (on: boolean) =>
  `rounded-md px-2 py-1 text-xs ${on ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`;

/**
 * Library sharing: read-only, each direction on its own. First what this device browses
 * (the Multi-device library, the devices it can browse, adding one), then what it shares
 * (the switch, who asks, the code, who can browse it). A device holding both grants is in
 * both lists, each with only that direction's actions — the device map's two lanes.
 */
export default function SharingTab() {
  const { t } = useTranslation();
  const {
    data,
    busy,
    run,
    confirm,
    setNotice,
    now,
    mounted,
    claimBackoff,
    sharingForm,
    anchor,
    revealed,
  } = useDevicesPage();
  const { status, access } = data;
  const [connectOpened, setConnectOpened] = useState(false);
  const connectFocus = useFocusOnOpen<HTMLInputElement>(connectOpened);

  if (!status) {
    return (
      <>
        <TabHeading introKey="federation.devices.tabIntro.sharing" />
        {data.sharingLoading && <p role="status">{t("federation.loading")}</p>}
      </>
    );
  }

  const {
    address,
    setAddress,
    code,
    setCode,
    shareBack,
    setShareBack,
    configureRemote,
    setConfigureRemote,
    invite,
    setInvite,
    discovered,
    setDiscovered,
  } = sharingForm;
  const remoteDisabled = status.remoteAccessMode === RemoteAccessMode.Disabled;
  // Enabling sharing while remote access is off would leave the device unreachable, so the
  // one-click default opens it; a mode the operator already widened is left alone.
  const configureRemoteChecked = remoteDisabled && (configureRemote ?? true);
  const inviteValid = !!invite && Date.parse(invite.expiresAt) > now;
  const outbound = status.peers.filter((peer) => peer.outboundGrant || !peer.inboundGrant);
  const inbound = status.peers.filter((peer) => peer.inboundGrant);
  const incoming = status.requests.filter((request) => request.direction === "incoming");
  const outgoing = status.requests.filter((request) => request.direction === "outgoing");
  const connectOpen = !outbound.length || connectOpened || anchor === "connect";
  // An unpaired browser can open this device whatever the sharing list says.
  const unpairedAccess =
    status.remoteAccessMode === RemoteAccessMode.Unrestricted ||
    (status.remoteAccessMode === RemoteAccessMode.Enabled && !status.requirePairing);
  const target = t<string>("federation.management.self");

  const showPairingOutcome = (result: PairingResult) => {
    if (!mounted.current) return;
    setNotice(t(`federation.pair.${result.outcome}`));
    if (result.outcome === "granted") setCode("");
  };
  const saveMappings = (peer: Peer) => (mappings: PathMapping[], expectedMappings: PathMapping[]) =>
    run(async () => {
      await federationPeerApi.mappings(peer.nodeId, mappings, expectedMappings);
      if (mounted.current) setNotice(t("federation.mappings.saved"));
    });
  const removePeer = (peer: Peer) =>
    confirm(
      t("federation.devices.remove"),
      t("federation.devices.removeConfirm", { name: peer.label }),
      () => federationPeerApi.remove(peer.nodeId),
    );
  const removeButton = (peer: Peer) => (
    <button
      className={`${buttonClass} text-danger`}
      disabled={busy}
      type="button"
      onClick={() => removePeer(peer)}
    >
      {t("federation.devices.remove")}
    </button>
  );

  return (
    <>
      <TabHeading introKey="federation.devices.tabIntro.sharing" />

      <section
        data-focus-section
        aria-labelledby="library-browse-title"
        className="space-y-3"
        id="library-browse"
      >
        <h3
          className="text-base font-semibold outline-none"
          id="library-browse-title"
          tabIndex={-1}
        >
          {t("federation.sharing.browseTitle")}
        </h3>

        <div
          className={`${panelClass} space-y-3 ${revealClass(revealed === "browsing")}`}
          data-highlighted={revealed === "browsing" || undefined}
          id="library-browsing"
          tabIndex={-1}
        >
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h4 className="font-medium">{t("federation.browsing.title")}</h4>
            <span className={chip(status.browsingEnabled === true)}>
              {t(
                status.browsingEnabled === true
                  ? "federation.browsing.on"
                  : "federation.browsing.off",
              )}
            </span>
          </div>
          <p className="text-sm text-default-500">{t("federation.browsing.description")}</p>
          {status.browsingEnabled === true && (
            <p className="text-xs text-default-500">{t("federation.browsing.disableTip")}</p>
          )}
          <div className="flex flex-wrap items-center gap-2">
            <button
              className={buttonClass}
              data-testid="browsing-switch"
              disabled={busy}
              type="button"
              onClick={() =>
                void run(() => federationPeerApi.browsing(status.browsingEnabled !== true))
              }
            >
              {t(
                status.browsingEnabled === true
                  ? "federation.browsing.disable"
                  : "federation.browsing.enable",
              )}
            </button>
            <Link className="text-xs text-primary underline" to="/federation">
              {t("federation.browsing.open")}
            </Link>
          </div>
        </div>

        <div className="space-y-2">
          <h4 className="text-sm font-medium">{t("federation.map.panel.self.browses")}</h4>
          <div className="space-y-2" data-testid="sharing-outbound">
            {!outbound.length && (
              <p className="rounded-lg bg-default-50 p-3 text-sm text-default-500">
                {t("federation.devices.empty")}
              </p>
            )}
            {outbound.map((peer) => (
              <article
                key={peer.nodeId}
                aria-label={peer.label}
                className={`${panelClass} space-y-3`}
                data-status={peer.outboundGrant ? "granted" : "none"}
                role="group"
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold">{peer.label}</p>
                    <p className="mt-1 break-all text-xs text-default-500">{peer.address}</p>
                  </div>
                  <span className="rounded-md bg-default-100 px-2 py-1 text-xs">
                    {peer.outboundGrant
                      ? t(`federation.connection.${peer.connectionState}`, {
                          defaultValue: peer.connectionState,
                        })
                      : t("federation.connection.Unauthorized")}
                  </span>
                </div>
                <div className="flex flex-wrap items-center gap-3">
                  <label className="mr-auto flex items-center gap-2 text-sm">
                    <input
                      checked={peer.enabled}
                      disabled={busy || !peer.outboundGrant}
                      type="checkbox"
                      onChange={(event) =>
                        void run(() => federationPeerApi.enable(peer.nodeId, event.target.checked))
                      }
                    />
                    {t("federation.devices.include")}
                  </label>
                  {peer.outboundGrant && (
                    <button
                      className={buttonClass}
                      disabled={busy}
                      type="button"
                      onClick={() =>
                        confirm(
                          t("federation.devices.forget"),
                          t("federation.devices.forgetConfirm", { name: peer.label }),
                          () => federationPeerApi.forget(peer.nodeId),
                        )
                      }
                    >
                      {t("federation.devices.forget")}
                    </button>
                  )}
                  {removeButton(peer)}
                </div>
                {peer.outboundGrant && (
                  <PeerPathMappings busy={busy} peer={peer} onSave={saveMappings(peer)} />
                )}
              </article>
            ))}
          </div>
        </div>

        {outgoing.length > 0 && (
          <div className="space-y-2" data-testid="sharing-outgoing-requests">
            <h4 className="text-sm font-medium">{t("federation.requests.outgoingTitle")}</h4>
            {outgoing.map((request) => (
              <RequestRow key={request.requestId} now={now} request={request}>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(async () => {
                      const result = await federationPeerApi.claim(request.requestId);

                      claimBackoff.current.delete(request.requestId);
                      showPairingOutcome(result);
                    })
                  }
                >
                  {t("federation.requests.check")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() => void run(() => federationPeerApi.cancelRequest(request.requestId))}
                >
                  {t("federation.requests.cancel")}
                </button>
              </RequestRow>
            ))}
          </div>
        )}

        <div
          className={`${panelClass} ${revealClass(revealed === "connect")}`}
          data-highlighted={revealed === "connect" || undefined}
          id="library-connect"
          tabIndex={-1}
        >
          {!connectOpen ? (
            <button
              aria-expanded={false}
              className={buttonClass}
              type="button"
              onClick={() => {
                connectFocus.request();
                setConnectOpened(true);
              }}
            >
              <AiOutlinePlus aria-hidden />
              {t("federation.devices.add")}
            </button>
          ) : (
            <>
              <h4 className="font-medium">{t("federation.devices.add")}</h4>
              <p className="mt-1 text-sm text-default-500">{t("federation.pair.description")}</p>
              <form
                className="mt-4 grid items-end gap-3 @3xl:grid-cols-[1fr_200px_auto]"
                onSubmit={(event) => {
                  event.preventDefault();
                  if (address.trim())
                    void run(async () =>
                      showPairingOutcome(
                        await federationPeerApi.connect(
                          address.trim(),
                          code.trim() || undefined,
                          shareBack,
                        ),
                      ),
                    );
                }}
              >
                <label className="space-y-1 text-sm">
                  <span>{t("federation.pair.address")}</span>
                  <input
                    ref={connectFocus.target}
                    required
                    className={fieldClass}
                    placeholder="192.168.1.5:34567"
                    value={address}
                    onChange={(event) => setAddress(event.target.value)}
                  />
                </label>
                <label className="space-y-1 text-sm">
                  <span>{t("federation.pair.code")}</span>
                  <input
                    autoComplete="off"
                    className={fieldClass}
                    value={code}
                    onChange={(event) => setCode(event.target.value)}
                  />
                </label>
                <button className={primaryClass} disabled={busy || !address.trim()} type="submit">
                  <AiOutlinePlus aria-hidden />
                  {t(code.trim() ? "federation.pair.withCode" : "federation.pair.request")}
                </button>
                <label className="col-span-full flex items-start gap-2 text-sm">
                  <input
                    checked={shareBack}
                    className="mt-1"
                    type="checkbox"
                    onChange={(event) => setShareBack(event.target.checked)}
                  />
                  <span>
                    {t("federation.pair.shareBack")}
                    <span className="mt-0.5 block text-xs text-default-500">
                      {t(
                        remoteDisabled
                          ? "federation.pair.shareBackTipRemote"
                          : "federation.pair.shareBackTip",
                      )}
                    </span>
                  </span>
                </label>
              </form>
              {!shareBack && (
                <p className="mt-3 text-xs text-default-500">{t("federation.pair.directionTip")}</p>
              )}
              <button
                className={`${buttonClass} mt-3`}
                disabled={busy}
                type="button"
                onClick={() =>
                  void run(async () => {
                    const found = await federationPeerApi.discover();

                    if (mounted.current) setDiscovered(found);
                  })
                }
              >
                {t("federation.discovery.scan")}
              </button>
              {/* The server leaves this device out. One listed under this device's own id is
                  another computer — a copy of its data folder — and adding it says so. */}
              {discovered && (
                <div className="mt-3 space-y-2" data-testid="sharing-candidates">
                  {!discovered.length && (
                    <p className="text-sm text-default-500">
                      {t("federation.discovery.noneFound")}
                    </p>
                  )}
                  {discovered.map((candidate) => (
                    <div
                      key={candidate.nodeId}
                      className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-default-50 p-3 text-sm"
                    >
                      <span>
                        {candidate.name}{" "}
                        <span className="text-default-500">{candidate.address}</span>
                      </span>
                      <button
                        className={buttonClass}
                        disabled={busy}
                        type="button"
                        onClick={() => {
                          setAddress(candidate.address);
                          setCode("");
                        }}
                      >
                        {t("federation.discovery.use")}
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </section>

      <section
        data-focus-section
        aria-labelledby="library-share-title"
        className={`space-y-3 ${revealClass(revealed === "share")}`}
        data-highlighted={revealed === "share" || undefined}
        id="library-share"
        tabIndex={-1}
      >
        <h3 className="text-base font-semibold outline-none" id="library-share-title" tabIndex={-1}>
          {t("federation.sharing.title")}
        </h3>

        <div className={`${panelClass} space-y-3`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm">{t("federation.sharing.description")}</p>
            <span className={chip(status.sharingEnabled)}>
              {t(status.sharingEnabled ? "federation.sharing.on" : "federation.sharing.off")}
            </span>
          </div>
          {!status.sharingEnabled && remoteDisabled && (
            <label className="flex items-start gap-2 rounded-lg bg-default-50 p-3 text-sm">
              <input
                checked={configureRemoteChecked}
                className="mt-1"
                type="checkbox"
                onChange={(event) => setConfigureRemote(event.target.checked)}
              />
              <span>{t("federation.sharing.configureRemote")}</span>
            </label>
          )}
          <button
            className={buttonClass}
            data-testid="sharing-switch"
            disabled={busy}
            type="button"
            onClick={() =>
              status.sharingEnabled
                ? confirm(
                    t("federation.sharing.stop"),
                    t("federation.sharing.stopConfirm"),
                    async () => {
                      await federationPeerApi.sharing(false, false);
                      if (mounted.current) setInvite(undefined);
                    },
                  )
                : confirm(
                    t("federation.sharing.start"),
                    t(
                      configureRemoteChecked
                        ? "federation.sharing.confirmWithRemote"
                        : "federation.sharing.confirm",
                    ),
                    () => federationPeerApi.sharing(true, configureRemoteChecked),
                  )
            }
          >
            {t(status.sharingEnabled ? "federation.sharing.stop" : "federation.sharing.start")}
          </button>
          {status.sharingEnabled && remoteDisabled && (
            <div className="space-y-2 rounded-lg border border-warning/40 bg-warning/10 p-3">
              <p className="text-sm">{t("federation.sharing.remoteDisabled")}</p>
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  // Sharing is already on: this only opens remote access, which also lets the
                  // devices already paired to manage this one back in, and says so.
                  confirm(
                    t("federation.sharing.enableRemote"),
                    t("federation.sharing.enableRemoteConfirm"),
                    () => federationPeerApi.sharing(true, true),
                  )
                }
              >
                {t("federation.sharing.enableRemote")}
              </button>
            </div>
          )}
        </div>

        {incoming.length > 0 && (
          <div
            className={`space-y-2 ${revealClass(revealed === "sharing-requests")}`}
            data-highlighted={revealed === "sharing-requests" || undefined}
            data-testid="sharing-requests"
            id="sharing-requests"
            tabIndex={-1}
          >
            <h4 className="text-sm font-medium">{t("federation.requests.incomingTitle")}</h4>
            {incoming.map((request) => {
              const expired = Date.parse(request.expiresAt) <= now;
              const pending = request.status === "awaitingApproval";
              // The name and node ID are the requester's own claims; the address is what we saw.
              const replaces = pending && request.replacesExistingAccess;
              const reciprocal = pending && request.offersReciprocalAccess;

              return (
                <RequestRow key={request.requestId} claim now={now} request={request}>
                  {pending && !expired && (
                    <>
                      <button
                        className={primaryClass}
                        disabled={busy}
                        type="button"
                        onClick={() =>
                          confirm(
                            t("federation.requests.approve"),
                            t(
                              request.remoteAddress
                                ? reciprocal
                                  ? "federation.requests.approveConfirmFromReciprocal"
                                  : "federation.requests.approveConfirmFrom"
                                : reciprocal
                                  ? "federation.requests.approveConfirmReciprocal"
                                  : "federation.requests.approveConfirm",
                              { name: request.nodeName, address: request.remoteAddress },
                            ),
                            () => federationPeerApi.decide(request.requestId, true),
                            replaces ? t("federation.requests.replacesExisting") : undefined,
                          )
                        }
                      >
                        {t("federation.requests.approve")}
                      </button>
                      <button
                        className={buttonClass}
                        disabled={busy}
                        type="button"
                        onClick={() =>
                          void run(() => federationPeerApi.decide(request.requestId, false))
                        }
                      >
                        {t("federation.requests.reject")}
                      </button>
                    </>
                  )}
                </RequestRow>
              );
            })}
          </div>
        )}

        {status.sharingEnabled && !remoteDisabled && (
          <InviteBlock
            action={
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  void run(async () => {
                    const issued = await federationPeerApi.invite();

                    if (mounted.current) setInvite(issued);
                  })
                }
              >
                {t("federation.sharing.issueCode")}
              </button>
            }
            // Read with remote access's settings, which may still be on their way or have
            // failed: never "no address found" for either.
            addresses={access?.addresses}
            addressesError={access ? undefined : data.accessError}
            code={inviteValid ? invite.code : undefined}
            context="sharing"
            status={
              invite &&
              (inviteValid ? (
                <p className="text-xs text-default-500">
                  {t("federation.expires", {
                    time: new Date(invite.expiresAt).toLocaleTimeString(),
                  })}
                </p>
              ) : (
                <p className="text-sm">{t("federation.sharing.codeExpired")}</p>
              ))
            }
            target={target}
            tip={t("federation.sharing.codeTip")}
            onRetryAddresses={() => void data.reload(["access"])}
          />
        )}

        <div className="space-y-2">
          <h4 className="text-sm font-medium">{t("federation.map.panel.self.sharesWith")}</h4>
          <div className="space-y-2" data-testid="sharing-inbound">
            {!inbound.length && (
              <p className="rounded-lg bg-default-50 p-3 text-sm text-default-500">
                {t("federation.devices.inboundEmpty")}
              </p>
            )}
            {inbound.map((peer) => (
              <article
                key={peer.nodeId}
                aria-label={peer.label}
                className={`${panelClass} flex flex-wrap items-center justify-between gap-3`}
                data-status="granted"
                role="group"
              >
                <div className="min-w-0">
                  <p className="font-semibold">{peer.label}</p>
                  <p className="mt-1 break-all text-xs text-default-500">{peer.address}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <button
                    className={`${buttonClass} text-danger`}
                    disabled={busy}
                    type="button"
                    onClick={() =>
                      peer.inboundGrant &&
                      confirm(
                        t("federation.devices.revoke"),
                        t("federation.devices.revokeConfirm", { name: peer.label }),
                        () => federationPeerApi.revoke(peer.inboundGrant!.grantId),
                      )
                    }
                  >
                    {t("federation.devices.revoke")}
                  </button>
                  {removeButton(peer)}
                </div>
              </article>
            ))}
          </div>
        </div>

        {unpairedAccess && (
          <p className="text-xs text-default-500">
            {t("federation.sharing.unpaired")}{" "}
            <Link className="text-primary underline" to={devicesRoute("management")}>
              {t("federation.devices.openManagementAccess")}
            </Link>
          </p>
        )}
      </section>
    </>
  );
}

/**
 * One library request. An incoming one is a claim: its name and id are the requester's own
 * words, so it is marked as not verified, and says the address it came from.
 */
function RequestRow({
  request,
  now,
  claim = false,
  children,
}: {
  request: PairingRequest;
  now: number;
  claim?: boolean;
  children?: ReactNode;
}) {
  const { t } = useTranslation();
  const expired = Date.parse(request.expiresAt) <= now;
  const pending = request.status === "awaitingApproval";
  const incoming = request.direction === "incoming";
  const live = pending && !expired;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-default-50 p-3 text-sm">
      <div className="min-w-0">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {request.nodeName}
          {claim && live && (
            <span className="rounded-md bg-warning/10 px-2 py-0.5 text-xs font-normal text-warning-600 dark:text-warning">
              {t("federation.map.unverified")}
            </span>
          )}
        </p>
        <p className="mt-1 text-xs text-default-500">
          {t(
            expired && pending
              ? "federation.requests.expired"
              : pending && incoming
                ? "federation.requests.awaitingYourApproval"
                : `federation.pair.${request.status}`,
          )}
        </p>
        {incoming && request.remoteAddress && (
          <p className="mt-1 break-all text-xs text-default-500">
            {t("federation.requests.from", { address: request.remoteAddress })}
          </p>
        )}
        {incoming && live && request.offersReciprocalAccess && (
          <p className="mt-1 text-xs text-success">
            {t("federation.requests.offersReciprocal", { name: request.nodeName })}
          </p>
        )}
        {incoming && live && request.replacesExistingAccess && (
          <p className="mt-2 max-w-2xl text-xs text-warning">
            {t("federation.requests.replacesExisting")}
          </p>
        )}
      </div>
      {live && <div className="flex flex-wrap gap-2">{children}</div>}
    </div>
  );
}
