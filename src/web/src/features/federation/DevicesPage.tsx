import type { PairingResult, PathMapping, Peer } from "./types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineLaptop, AiOutlinePlus, AiOutlineReload } from "react-icons/ai";

import {
  buttonClass,
  ErrorNotice,
  FederationAccess,
  fieldClass,
  panelClass,
  primaryClass,
} from "./components/common";
import { useFederationStatus } from "./hooks/useFederationStatus";
import { federationPeerApi } from "./peerApi";
import ImportConnectionHints from "./components/ImportConnectionHints";

export default function DevicesPage() {
  return (
    <FederationAccess>
      <Devices />
    </FederationAccess>
  );
}

function Devices() {
  const { t } = useTranslation();
  const { status, error: loadError, loading, refresh } = useFederationStatus();
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [notice, setNotice] = useState<string>();
  const [invite, setInvite] = useState<{ code: string; expiresAt: string }>();
  const [configureRemote, setConfigureRemote] = useState(false);
  const [discovered, setDiscovered] =
    useState<{ nodeId: string; name: string; address: string }[]>();
  const [confirmation, setConfirmation] = useState<{
    title: string;
    description: string;
    action: () => Promise<unknown>;
  }>();
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    mounted.current = true;
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => {
      mounted.current = false;
      clearInterval(timer);
    };
  }, []);

  const run = async (operation: () => Promise<unknown>) => {
    if (busyRef.current) return false;
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      await operation();
      if (mounted.current) await refresh();

      return true;
    } catch (cause) {
      if (mounted.current) setError(cause instanceof Error ? cause : new Error(String(cause)));

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  const showPairingOutcome = (result: PairingResult) => {
    if (!mounted.current) return;
    setNotice(t(`federation.pair.${result.outcome}`));
    if (result.outcome === "granted") setCode("");
  };

  const hasPending = status?.requests.some(
    (request) => request.status === "awaitingApproval" && Date.parse(request.expiresAt) > now,
  );

  useEffect(() => {
    if (!hasPending) return;
    const timer = setInterval(() => {
      if (document.hidden || busyRef.current) return;
      void run(async () => {
        const outgoing =
          status?.requests.filter(
            (request) =>
              request.direction === "outgoing" &&
              request.status === "awaitingApproval" &&
              Date.parse(request.expiresAt) > Date.now(),
          ) ?? [];

        for (const request of outgoing) {
          const result = await federationPeerApi.claim(request.requestId);

          if (result.outcome !== "awaitingApproval") showPairingOutcome(result);
        }
      });
    }, 4000);

    return () => clearInterval(timer);
  }, [hasPending, status?.requests]);

  const confirm = (title: string, description: string, action: () => Promise<unknown>) =>
    setConfirmation({ title, description, action });
  const inviteValid = invite && Date.parse(invite.expiresAt) > now;

  return (
    <div className="mx-auto flex max-w-[1200px] flex-col gap-5 p-4 sm:p-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineLaptop aria-hidden />
            {t("federation.devices.title")}
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("federation.devices.intro")}</p>
        </div>
        <div className="flex gap-2">
          <button
            className={buttonClass}
            disabled={loading || busy}
            type="button"
            onClick={() => void refresh()}
          >
            <AiOutlineReload aria-hidden />
            {t("federation.refresh")}
          </button>
          <Link className={buttonClass} to="/federation">
            {t("federation.title")}
          </Link>
        </div>
      </header>
      <ErrorNotice error={loadError} onRetry={() => void refresh()} />
      <ErrorNotice error={error} />
      {notice && (
        <p className="rounded-lg bg-primary/10 p-3 text-sm" role="status">
          {notice}
        </p>
      )}
      {confirmation && (
        <section
          aria-label={confirmation.title}
          className="rounded-xl border border-warning/40 bg-warning/5 p-4"
          role="alertdialog"
        >
          <h2 className="font-semibold">{confirmation.title}</h2>
          <p className="mt-2 text-sm">{confirmation.description}</p>
          <div className="mt-3 flex gap-2">
            <button
              className={primaryClass}
              disabled={busy}
              type="button"
              onClick={() =>
                void run(async () => {
                  await confirmation.action();
                  if (mounted.current) setConfirmation(undefined);
                })
              }
            >
              {t("federation.confirm")}
            </button>
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => setConfirmation(undefined)}
            >
              {t("federation.cancel")}
            </button>
          </div>
        </section>
      )}
      {!status && loading && <p role="status">{t("federation.loading")}</p>}
      {status && (
        <>
          <section className={`${panelClass} space-y-3`}>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-xs text-default-500">{t("federation.thisDevice")}</p>
                <h2 className="text-lg font-semibold">{status.identity.name}</h2>
                <p className="mt-1 break-all font-mono text-xs text-default-400">
                  {status.identity.nodeId}
                </p>
              </div>
              <span
                className={`rounded-md px-2 py-1 text-xs ${status.sharingEnabled ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`}
              >
                {t(status.sharingEnabled ? "federation.sharing.on" : "federation.sharing.off")}
              </span>
            </div>
            <p className="text-sm">{t("federation.sharing.description")}</p>
            <p className="text-xs text-default-500">
              {t("federation.sharing.mode", {
                mode: t(`federation.remoteMode.${status.remoteAccessMode}`),
              })}{" "}
              ·{" "}
              {t(
                status.requirePairing ? "federation.sharing.paired" : "federation.sharing.unpaired",
              )}
            </p>
            {!status.sharingEnabled && (
              <label className="flex items-start gap-2 rounded-lg bg-default-50 p-3 text-sm">
                <input
                  checked={configureRemote}
                  className="mt-1"
                  type="checkbox"
                  onChange={(event) => setConfigureRemote(event.target.checked)}
                />
                <span>{t("federation.sharing.configureRemote")}</span>
              </label>
            )}
            <div className="flex flex-wrap gap-2">
              <button
                className={buttonClass}
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
                    : confirm(t("federation.sharing.start"), t("federation.sharing.confirm"), () =>
                        federationPeerApi.sharing(true, configureRemote),
                      )
                }
              >
                {t(status.sharingEnabled ? "federation.sharing.stop" : "federation.sharing.start")}
              </button>
              <button
                className={buttonClass}
                disabled={busy || !status.sharingEnabled}
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
            </div>
            {invite && (
              <div className="rounded-lg border border-default-200 p-3">
                {inviteValid ? (
                  <>
                    <p className="text-xs text-default-500">{t("federation.sharing.codeTip")}</p>
                    <code className="my-2 block text-2xl tracking-[0.25em]">{invite.code}</code>
                    <p className="text-xs text-default-500">
                      {t("federation.expires", {
                        time: new Date(invite.expiresAt).toLocaleTimeString(),
                      })}
                    </p>
                  </>
                ) : (
                  <p className="text-sm">{t("federation.sharing.codeExpired")}</p>
                )}
              </div>
            )}
            {status.sharingEnabled && status.remoteAccessMode === 0 && (
              <div className="space-y-2">
                <p className="text-sm text-warning">{t("federation.sharing.remoteDisabled")}</p>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    confirm(
                      t("federation.sharing.configureRemote"),
                      t("federation.sharing.confirm"),
                      () => federationPeerApi.sharing(true, true),
                    )
                  }
                >
                  {t("federation.sharing.configureRemote")}
                </button>
              </div>
            )}
          </section>
          <section className={panelClass}>
            <h2 className="font-semibold">{t("federation.devices.add")}</h2>
            <p className="mt-1 text-sm text-default-500">{t("federation.pair.description")}</p>
            <form
              className="mt-4 grid items-end gap-3 md:grid-cols-[1fr_200px_auto_auto]"
              onSubmit={(event) => {
                event.preventDefault();
                if (address.trim())
                  void run(async () =>
                    showPairingOutcome(
                      await federationPeerApi.connect(address.trim(), code.trim() || undefined),
                    ),
                  );
              }}
            >
              <label className="space-y-1 text-sm">
                <span>{t("federation.pair.address")}</span>
                <input
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
            </form>
            <p className="mt-3 text-xs text-default-500">{t("federation.pair.directionTip")}</p>
            <button
              className={`${buttonClass} mt-3`}
              disabled={busy}
              type="button"
              onClick={() =>
                void run(async () => {
                  const candidates = await federationPeerApi.discover();

                  if (mounted.current) setDiscovered(candidates);
                })
              }
            >
              {t("federation.discovery.scan")}
            </button>
            {discovered && (
              <div className="mt-3 space-y-2">
                {!discovered.length && (
                  <p className="text-sm text-default-500">{t("federation.discovery.none")}</p>
                )}
                {discovered
                  .filter((candidate) => candidate.nodeId !== status.identity.nodeId)
                  .map((candidate) => (
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
          </section>
          {status.requests.length > 0 && (
            <section className={panelClass}>
              <h2 className="font-semibold">{t("federation.requests.title")}</h2>
              <div className="mt-3 divide-y divide-default-200">
                {status.requests.map((request) => {
                  const expired = Date.parse(request.expiresAt) <= now;
                  const pending = request.status === "awaitingApproval";

                  return (
                    <div
                      key={request.requestId}
                      className="flex flex-wrap items-center justify-between gap-3 py-3"
                    >
                      <div>
                        <p className="font-medium">{request.nodeName}</p>
                        <p className="mt-1 text-xs text-default-500">
                          {t(`federation.requests.${request.direction}`)} ·{" "}
                          {t(
                            expired && pending
                              ? "federation.requests.expired"
                              : `federation.pair.${request.status}`,
                          )}
                        </p>
                      </div>
                      {pending && !expired && (
                        <div className="flex gap-2">
                          {request.direction === "incoming" ? (
                            <>
                              <button
                                className={primaryClass}
                                disabled={busy}
                                type="button"
                                onClick={() =>
                                  confirm(
                                    t("federation.requests.approve"),
                                    t("federation.requests.approveConfirm", {
                                      name: request.nodeName,
                                    }),
                                    () => federationPeerApi.decide(request.requestId, true),
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
                          ) : (
                            <button
                              className={buttonClass}
                              disabled={busy}
                              type="button"
                              onClick={() =>
                                void run(async () =>
                                  showPairingOutcome(
                                    await federationPeerApi.claim(request.requestId),
                                  ),
                                )
                              }
                            >
                              {t("federation.requests.check")}
                            </button>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </section>
          )}
          <ImportConnectionHints
            busy={busy}
            onSelect={(address) => {
              setAddress(address);
              setCode("");
            }}
          />
          <section className="space-y-3">
            <div className="flex items-baseline justify-between">
              <h2 className="font-semibold">{t("federation.devices.known")}</h2>
              <span className="text-xs text-default-500">{status.peers.length}</span>
            </div>
            {!status.peers.length && (
              <div className={`${panelClass} py-8 text-center text-sm text-default-500`}>
                {t("federation.devices.empty")}
              </div>
            )}
            {status.peers.map((peer) => (
              <PeerCard
                key={peer.nodeId}
                busy={busy}
                peer={peer}
                onEnable={(enabled) =>
                  void run(() => federationPeerApi.enable(peer.nodeId, enabled))
                }
                onForget={() =>
                  confirm(
                    t("federation.devices.forget"),
                    t("federation.devices.forgetConfirm", { name: peer.label }),
                    () => federationPeerApi.forget(peer.nodeId),
                  )
                }
                onRevoke={() =>
                  peer.inboundGrant &&
                  confirm(
                    t("federation.devices.revoke"),
                    t("federation.devices.revokeConfirm", { name: peer.label }),
                    () => federationPeerApi.revoke(peer.inboundGrant!.grantId),
                  )
                }
                onSaveMappings={(mappings) =>
                  run(async () => {
                    await federationPeerApi.mappings(peer.nodeId, mappings);
                    if (mounted.current) setNotice(t("federation.mappings.saved"));
                  })
                }
              />
            ))}
          </section>
          <details className={panelClass}>
            <summary className="cursor-pointer text-sm font-medium">
              {t("federation.identity.title")}
            </summary>
            <p className="mt-2 text-sm text-default-500">{t("federation.identity.tip")}</p>
            <button
              className={`${buttonClass} mt-3 text-danger`}
              disabled={busy}
              type="button"
              onClick={() =>
                confirm(
                  t("federation.identity.reset"),
                  t("federation.identity.confirm"),
                  async () => {
                    await federationPeerApi.resetIdentity();
                    if (mounted.current) setInvite(undefined);
                  },
                )
              }
            >
              {t("federation.identity.reset")}
            </button>
          </details>
        </>
      )}
    </div>
  );
}

function PeerCard({
  peer,
  busy,
  onEnable,
  onForget,
  onRevoke,
  onSaveMappings,
}: {
  peer: Peer;
  busy: boolean;
  onEnable: (enabled: boolean) => void;
  onForget: () => void;
  onRevoke: () => void;
  onSaveMappings: (mappings: PathMapping[]) => Promise<boolean>;
}) {
  const { t } = useTranslation();
  const [mappings, setMappings] = useState(peer.pathMappings);
  const [dirty, setDirty] = useState(false);
  const [roots, setRoots] = useState<{ sourceRootId: string; name: string }[]>();
  const [rootsError, setRootsError] = useState<Error>();
  const [loadingRoots, setLoadingRoots] = useState(false);
  const rootRequest = useRef<AbortController>();

  useEffect(() => () => rootRequest.current?.abort(), []);
  const loadRoots = async () => {
    rootRequest.current?.abort();
    const request = new AbortController();

    rootRequest.current = request;
    setLoadingRoots(true);
    setRootsError(undefined);
    try {
      const result = await federationPeerApi.mappingRoots(peer.nodeId, request.signal);

      if (!request.signal.aborted) setRoots(result);
    } catch (cause) {
      if (!request.signal.aborted)
        setRootsError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (!request.signal.aborted) setLoadingRoots(false);
    }
  };

  useEffect(() => {
    if (!dirty) setMappings(peer.pathMappings);
  }, [peer.pathMappings, dirty]);
  const update = (index: number, patch: Partial<PathMapping>) => {
    setDirty(true);
    setMappings((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  };

  return (
    <article className={`${panelClass} space-y-3`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-semibold">{peer.label}</h3>
          <p className="mt-1 break-all text-xs text-default-500">{peer.address}</p>
        </div>
        <span className="rounded-md bg-default-100 px-2 py-1 text-xs">
          {t(`federation.connection.${peer.connectionState}`, {
            defaultValue: peer.connectionState,
          })}
        </span>
      </div>
      <div className="flex flex-wrap gap-2 text-xs">
        <span
          className={`rounded-md px-2 py-1 ${peer.outboundGrant ? "bg-success/10 text-success" : "bg-default-100 text-default-500"}`}
        >
          {t(peer.outboundGrant ? "federation.devices.outbound" : "federation.devices.noOutbound")}
        </span>
        <span
          className={`rounded-md px-2 py-1 ${peer.inboundGrant ? "bg-primary/10 text-primary" : "bg-default-100 text-default-500"}`}
        >
          {t(peer.inboundGrant ? "federation.devices.inbound" : "federation.devices.noInbound")}
        </span>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <label className="mr-auto flex items-center gap-2 text-sm">
          <input
            checked={peer.enabled}
            disabled={busy || !peer.outboundGrant}
            type="checkbox"
            onChange={(event) => onEnable(event.target.checked)}
          />
          {t("federation.devices.include")}
        </label>
        {peer.outboundGrant && (
          <button className={buttonClass} disabled={busy} type="button" onClick={onForget}>
            {t("federation.devices.forget")}
          </button>
        )}
        {peer.inboundGrant && (
          <button
            className={`${buttonClass} text-danger`}
            disabled={busy}
            type="button"
            onClick={onRevoke}
          >
            {t("federation.devices.revoke")}
          </button>
        )}
      </div>
      {peer.outboundGrant && (
        <details
          className="border-t border-default-200 pt-3"
          onToggle={(event) => {
            if (event.currentTarget.open && !roots && !loadingRoots) void loadRoots();
          }}
        >
          <summary className="cursor-pointer text-sm font-medium">
            {t("federation.mappings.title")}
          </summary>
          <p className="mt-2 text-xs text-default-500">{t("federation.mappings.tip")}</p>
          <ErrorNotice error={rootsError} onRetry={() => void loadRoots()} />
          {loadingRoots && (
            <p className="mt-2 text-xs" role="status">
              {t("federation.loading")}
            </p>
          )}
          <div className="mt-3 space-y-2">
            {mappings.map((mapping, index) => (
              <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
                <label className="space-y-1 text-xs">
                  <span>{t("federation.mappings.root")}</span>
                  <select
                    className={fieldClass}
                    value={mapping.sourceRootId}
                    onChange={(event) => update(index, { sourceRootId: event.target.value })}
                  >
                    <option value="">{t("federation.mappings.selectRoot")}</option>
                    {mapping.sourceRootId &&
                      !roots?.some((root) => root.sourceRootId === mapping.sourceRootId) && (
                        <option value={mapping.sourceRootId}>
                          {t("federation.mappings.unavailableRoot")}
                        </option>
                      )}
                    {roots?.map((root) => (
                      <option key={root.sourceRootId} value={root.sourceRootId}>
                        {root.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="space-y-1 text-xs">
                  <span>{t("federation.mappings.local")}</span>
                  <input
                    className={fieldClass}
                    placeholder="/Volumes/Media"
                    value={mapping.localPath}
                    onChange={(event) => update(index, { localPath: event.target.value })}
                  />
                </label>
                <button
                  aria-label={t("federation.mappings.remove")}
                  className={`${buttonClass} self-end`}
                  disabled={busy}
                  type="button"
                  onClick={() => {
                    setDirty(true);
                    setMappings((rows) => rows.filter((_, i) => i !== index));
                  }}
                >
                  ×
                </button>
              </div>
            ))}
          </div>
          <div className="mt-3 flex gap-2">
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => {
                setDirty(true);
                setMappings((rows) => [...rows, { sourceRootId: "", localPath: "" }]);
              }}
            >
              {t("federation.mappings.add")}
            </button>
            <button
              className={primaryClass}
              disabled={
                busy ||
                !dirty ||
                mappings.some(
                  (mapping) => !mapping.sourceRootId.trim() || !mapping.localPath.trim(),
                )
              }
              type="button"
              onClick={() =>
                void onSaveMappings(
                  mappings.map((mapping) => ({
                    sourceRootId: mapping.sourceRootId.trim(),
                    localPath: mapping.localPath.trim(),
                  })),
                ).then((saved) => {
                  if (saved) setDirty(false);
                })
              }
            >
              {t("federation.save")}
            </button>
          </div>
        </details>
      )}
    </article>
  );
}
