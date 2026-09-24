import type { Ref } from "react";
import type {
  ManagedServer,
  ManagedServerCandidate,
  ManagedServerPairing,
  ManagedServerPathMapping,
  ManagedServerPendingRequest,
  ManagedServersView,
} from "../types";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCloudServer, AiOutlineLoading3Quarters, AiOutlinePlus } from "react-icons/ai";

import { revealClass } from "../hooks/useSectionReveal";
import { managedServerApi } from "../serverApi";
import { CONFIGURATION_ROUTE, openManagedServer } from "../switching";
import { FederationError, isAbort } from "../transport";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  fieldClass,
  panelClass,
  primaryClass,
} from "./common";
import ConfirmDialog from "./ConfirmDialog";

import { minutesUntil } from "@/core/serverTime";
import {
  ManagedServerOutcome,
  ManagedServerOutcomeLabel,
  ManagedServerState,
  RemoteAccessMode,
} from "@/sdk/constants";

/** While a filed request waits, the list is re-read this often to pick up the answer. */
const REQUEST_POLL_MS = 5000;

interface Confirmation {
  title: string;
  description: string;
  warning?: string;
  action: () => Promise<unknown>;
}

/**
 * An outcome the server reported rather than an error it threw — "wrong code", "nothing
 * answered". Carried as a {@link FederationError} so it renders like every other failure
 * on this page, with its own explanation.
 */
const outcomeError = (outcome: ManagedServerOutcome, detail?: string | null) =>
  new FederationError(
    `ManagedServer${ManagedServerOutcomeLabel[outcome] ?? outcome}`,
    detail ?? "",
    0,
  );

const stateBadgeClass: Record<ManagedServerState, string> = {
  [ManagedServerState.Unknown]: "bg-default-100 text-default-500",
  [ManagedServerState.Online]: "bg-success/10 text-success",
  [ManagedServerState.Offline]: "bg-default-100 text-default-500",
  [ManagedServerState.Revoked]: "bg-danger/10 text-danger",
  [ManagedServerState.WrongServer]: "bg-warning/10 text-warning-600 dark:text-warning",
};

const serverLabel = (server: Pick<ManagedServer, "name" | "address">) =>
  server.name || server.address;

const requestLabel = (request: Pick<ManagedServerPendingRequest, "serverName" | "address">) =>
  request.serverName || request.address;

/**
 * Other devices this one manages in full — the removed thin client's job, now done by
 * switching this window to the other device's own UI.
 *
 * Kept apart from the read-only library sharing below it on purpose: this is the other
 * device's administrator access, with its own pairing, and nothing here grants or uses a
 * sharing permission.
 */
export default function ManagedServersSection({
  onSettled,
  sectionRef,
  highlighted = false,
}: {
  /** Called once, when the first listing has finished — either way. */
  onSettled?: () => void;
  /** The section element, for a page that brings it into view. */
  sectionRef?: Ref<HTMLElement>;
  /** Marks the section for a moment after a link led here. */
  highlighted?: boolean;
}) {
  const { t } = useTranslation();
  const [view, setView] = useState<ManagedServersView>();
  const [loadError, setLoadError] = useState<Error>();
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState<string>();
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [discovered, setDiscovered] = useState<ManagedServerCandidate[]>();
  const [discovering, setDiscovering] = useState(false);
  const [discoverError, setDiscoverError] = useState<Error>();
  /** The search under way, so leaving the page can stop listening for its answer. */
  const discovery = useRef<AbortController>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  const generation = useRef(0);
  /**
   * What the last listing held, so an answer arriving between two reads can be named.
   * Only live requests: one that already shows how it ended is not news when it goes.
   */
  const previous = useRef<{ servers: Set<string>; live: Map<string, string> }>();
  /** Requests this page withdrew itself; their disappearance is not news. */
  const withdrawn = useRef(new Set<string>());
  const latestT = useRef(t);
  const latestSettled = useRef(onSettled);
  const settled = useRef(false);

  latestT.current = t;
  latestSettled.current = onSettled;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      generation.current += 1;
      discovery.current?.abort();
    };
  }, []);

  const accept = useCallback((fresh: ManagedServersView) => {
    const before = previous.current;

    if (before) {
      const added = fresh.servers.filter((server) => !before.servers.has(server.serverId));
      // Requests that were live and now are not listed at all. One that ended with an
      // answer stays listed for a while and says so in its own row; one that is gone was
      // either approved — its server is new above — or ended somewhere this page cannot see.
      const gone = [...before.live.entries()].filter(
        ([requestId]) =>
          !withdrawn.current.has(requestId) &&
          !fresh.requests.some((request) => request.requestId === requestId),
      );

      if (gone.length) {
        setNotice(
          added.length
            ? latestT.current("federation.servers.approved", {
                name: added.map(serverLabel).join(", "),
              })
            : latestT.current("federation.servers.requestClosed", { name: gone[0][1] }),
        );
      }
    }
    previous.current = {
      servers: new Set(fresh.servers.map((server) => server.serverId)),
      live: new Map(
        fresh.requests
          .filter((request) => request.active)
          .map((request) => [request.requestId, requestLabel(request)]),
      ),
    };
    setView(fresh);
  }, []);

  /**
   * `quiet` is for polling and the follow-up probe: no loading state, and a failure keeps
   * the last good listing instead of replacing it with an error.
   */
  const load = useCallback(
    async (options: { probe?: boolean; quiet?: boolean } = {}) => {
      const run = ++generation.current;

      if (!options.quiet) setLoading(true);
      try {
        const fresh = await managedServerApi.list(options.probe === true);

        if (run !== generation.current) return;
        accept(fresh);
        setLoadError(undefined);
      } catch (cause) {
        if (run === generation.current && !options.quiet)
          setLoadError(cause instanceof Error ? cause : new Error(String(cause)));
      } finally {
        if (run === generation.current && !options.quiet) setLoading(false);
        if (!settled.current && mounted.current) {
          settled.current = true;
          latestSettled.current?.();
        }
      }
    },
    [accept],
  );

  useEffect(() => {
    // A plain listing first so the page fills at once, then the probed one for states.
    void load().then(() => {
      if (mounted.current) void load({ probe: true, quiet: true });
    });
  }, [load]);

  // Live, not "awaiting approval": after one failed attempt the outcome reads Unreachable
  // while the app keeps asking, and the approval can still arrive.
  const waiting = (view?.requests ?? []).some((request) => request.active);

  // The app collects an approval in the background; this only re-reads the list to show it.
  useEffect(() => {
    if (!waiting) return;
    const timer = setInterval(() => {
      if (!document.hidden && !busyRef.current) void load({ quiet: true });
    }, REQUEST_POLL_MS);

    return () => clearInterval(timer);
  }, [waiting, load]);

  /** User actions: one at a time, the list re-read afterwards, failures shown by `onError`. */
  const run = async (
    operation: () => Promise<unknown>,
    onError: (cause: Error) => void = setError,
    reload = true,
  ) => {
    if (busyRef.current) return false;
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    setConfirmationError(undefined);
    try {
      await operation();
      if (mounted.current && reload) await load({ quiet: true });

      return true;
    } catch (cause) {
      if (mounted.current) onError(cause instanceof Error ? cause : new Error(String(cause)));

      return false;
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  const confirm = (
    title: string,
    description: string,
    action: () => Promise<unknown>,
    warning?: string,
  ) => {
    setConfirmationError(undefined);
    setConfirmation({ title, description, warning, action });
  };

  /** Says what pairing did; true when the server is managed from now on. */
  const showPairing = (result: ManagedServerPairing, target: string) => {
    const name = result.serverName || target;

    switch (result.outcome) {
      case ManagedServerOutcome.Ok:
        setNotice(t("federation.servers.paired", { name }));
        setAddress("");
        setCode("");

        return true;
      case ManagedServerOutcome.AwaitingApproval:
        setNotice(t("federation.servers.requested", { name }));
        setAddress("");
        setCode("");

        return false;
      default:
        throw outcomeError(result.outcome, result.detail);
    }
  };

  const pair = async (target: string, pairingCode?: string) => {
    let paired = false;
    const ok = await run(async () => {
      paired = showPairing(await managedServerApi.pair(target, pairingCode), target);
    });

    // The re-read after it shows the new server; a probe then says how it is — online,
    // and whether it lets anybody in without pairing.
    if (ok && paired && mounted.current) void load({ probe: true, quiet: true });
  };

  /**
   * Listens for servers announcing themselves — a few seconds, so it runs beside the
   * other actions rather than blocking them, and says it is still looking.
   */
  const discover = async () => {
    if (discovery.current) return;
    const controller = new AbortController();

    discovery.current = controller;
    setDiscovering(true);
    setDiscoverError(undefined);
    try {
      const found = await managedServerApi.discover(controller.signal);

      if (!controller.signal.aborted) setDiscovered(found.servers ?? []);
    } catch (cause) {
      if (controller.signal.aborted || isAbort(cause)) return;
      setDiscovered(undefined);
      setDiscoverError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (discovery.current === controller) discovery.current = undefined;
      if (!controller.signal.aborted) setDiscovering(false);
    }
  };

  if (view && !view.available) return null;

  const servers = view?.servers ?? [];
  const requests = view?.requests ?? [];
  // The server marks what it already manages; a server paired since the search is too.
  const managedHere = (candidate: ManagedServerCandidate) =>
    candidate.alreadyManaged || servers.some((server) => server.serverId === candidate.serverId);

  return (
    <section
      ref={sectionRef}
      aria-busy={loading || undefined}
      aria-labelledby="managed-servers-title"
      className={`${panelClass} space-y-4 ${revealClass(highlighted)}`}
      data-highlighted={highlighted || undefined}
      id="managed-servers"
      tabIndex={-1}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="flex items-center gap-2 font-semibold" id="managed-servers-title">
            <AiOutlineCloudServer aria-hidden />
            {t("federation.servers.title")}
          </h2>
          <p className="mt-1 max-w-3xl text-sm text-default-500">
            {t("federation.servers.description")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            className={buttonClass}
            disabled={busy || loading}
            type="button"
            onClick={() => void run(() => load({ probe: true }), setError, false)}
          >
            {t("federation.servers.refresh")}
          </button>
          <button
            className={buttonClass}
            disabled={busy}
            type="button"
            onClick={() =>
              void run(async () => {
                const result = await managedServerApi.importLegacyClient();

                if (!mounted.current) return;
                setNotice(
                  !result.found
                    ? t("federation.servers.import.notFound")
                    : result.imported > 0
                      ? t("federation.servers.import.done", {
                          imported: result.imported,
                          skipped: result.skipped,
                        })
                      : t("federation.servers.import.nothingNew", { skipped: result.skipped }),
                );
              })
            }
          >
            {t("federation.servers.import.action")}
          </button>
        </div>
      </div>
      {(error || notice) && (
        <div className="space-y-2">
          <ErrorNotice error={error} onDismiss={() => setError(undefined)} />
          {notice && (
            <div
              className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
              role="status"
            >
              <p>{notice}</p>
              <DismissButton onClick={() => setNotice(undefined)} />
            </div>
          )}
        </div>
      )}
      <ErrorNotice error={loadError} onRetry={() => void load()} />
      {!view && loading && <p className="text-sm">{t("federation.loading")}</p>}
      {view && !servers.length && (
        <p className="rounded-lg bg-default-50 p-3 text-sm text-default-500">
          {t("federation.servers.empty")}
        </p>
      )}
      {servers.length > 0 && (
        <div className="space-y-3">
          {servers.map((server) => (
            <ManagedServerCard
              key={server.serverId}
              busy={busy}
              server={server}
              onForget={() =>
                confirm(
                  t("federation.servers.forget"),
                  t("federation.servers.forgetConfirm", { name: serverLabel(server) }),
                  () => managedServerApi.forget(server.serverId),
                )
              }
              onOpen={(route) =>
                // No reload: success navigates away, and a failure leaves the list as it was.
                void run(() => openManagedServer(server.serverId, route), setError, false)
              }
              onSaveMappings={(mappings) =>
                run(async () => {
                  await managedServerApi.setPathMappings(server.serverId, mappings);
                  if (mounted.current) setNotice(t("federation.servers.mappings.saved"));
                })
              }
            />
          ))}
        </div>
      )}
      {requests.length > 0 && (
        <div className="space-y-2" data-testid="managed-server-requests">
          {requests.map((request) => {
            const name = requestLabel(request);
            const minutes = minutesUntil(request.expiresAt);
            // A live request whose last attempt failed is still live — the app keeps
            // asking until it expires — so the failure is a note, not the answer.
            const retrying =
              request.active && request.outcome !== ManagedServerOutcome.AwaitingApproval;

            return (
              <div
                key={request.requestId}
                className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-default-50 p-3 text-sm"
                data-active={request.active || undefined}
              >
                <div className="min-w-0">
                  <p className="font-medium">{name}</p>
                  <p
                    className={`mt-1 text-xs ${retrying ? "text-warning-600 dark:text-warning" : "text-default-500"}`}
                  >
                    {!request.active
                      ? t(
                          `federation.error.ManagedServer${ManagedServerOutcomeLabel[request.outcome] ?? request.outcome}`,
                        )
                      : retrying
                        ? t("federation.servers.retrying", { name, minutes })
                        : t("federation.servers.waiting", { name, minutes })}
                  </p>
                </div>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(async () => {
                      withdrawn.current.add(request.requestId);
                      await managedServerApi.cancelRequest(request.requestId);
                    })
                  }
                >
                  {t(
                    request.active
                      ? "federation.servers.cancelRequest"
                      : "federation.servers.dismiss",
                  )}
                </button>
              </div>
            );
          })}
        </div>
      )}
      <div className="border-t border-default-200 pt-4">
        <h3 className="font-medium">{t("federation.servers.add.title")}</h3>
        <p className="mt-1 text-sm text-default-500">{t("federation.servers.add.description")}</p>
        <form
          className="mt-3 grid items-end gap-3 md:grid-cols-[1fr_200px_auto]"
          onSubmit={(event) => {
            event.preventDefault();
            const target = address.trim();

            if (target) void pair(target, code.trim() || undefined);
          }}
        >
          <label className="space-y-1 text-sm">
            <span>{t("federation.servers.add.address")}</span>
            <input
              required
              className={fieldClass}
              placeholder="http://192.168.1.5:34567"
              value={address}
              onChange={(event) => setAddress(event.target.value)}
            />
          </label>
          <label className="space-y-1 text-sm">
            <span>{t("federation.servers.add.code")}</span>
            <input
              autoComplete="off"
              className={fieldClass}
              value={code}
              onChange={(event) => setCode(event.target.value)}
            />
          </label>
          <button className={primaryClass} disabled={busy || !address.trim()} type="submit">
            <AiOutlinePlus aria-hidden />
            {t(code.trim() ? "federation.servers.add.withCode" : "federation.servers.add.request")}
          </button>
        </form>
        <p className="mt-2 text-xs text-default-500">{t("federation.servers.add.tip")}</p>
        <button
          aria-busy={discovering || undefined}
          className={`${buttonClass} mt-3`}
          disabled={discovering}
          type="button"
          onClick={() => void discover()}
        >
          {discovering && <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />}
          {t(
            discovering ? "federation.servers.add.discovering" : "federation.servers.add.discover",
          )}
        </button>
        {discoverError && (
          <div className="mt-3">
            <ErrorNotice error={discoverError} onRetry={() => void discover()} />
          </div>
        )}
        {discovered && !discovering && (
          <div className="mt-3 space-y-2" data-testid="managed-server-candidates">
            {!discovered.length && (
              <p className="text-sm text-default-500">{t("federation.servers.add.noneFound")}</p>
            )}
            {discovered.map((candidate) => {
              const managed = managedHere(candidate);

              return (
                <div
                  key={candidate.serverId}
                  aria-label={candidate.name || candidate.address}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-default-50 p-3 text-sm"
                  role="group"
                >
                  <span className="min-w-0 break-all">
                    {candidate.name || candidate.address}{" "}
                    <span className="text-default-500">
                      {candidate.address}
                      {candidate.appVersion ? ` · v${candidate.appVersion}` : ""}
                    </span>
                  </span>
                  {managed ? (
                    // Found, and said so — a search that silently left it out would read as
                    // "not on this network" — but not offered again: it is in the list above.
                    <span className="text-xs text-default-500">
                      {t("federation.servers.add.alreadyManaged")}
                    </span>
                  ) : (
                    <button
                      className={buttonClass}
                      disabled={busy}
                      type="button"
                      onClick={() => {
                        setAddress(candidate.address);
                        setCode("");
                      }}
                    >
                      {t("federation.servers.add.useAddress")}
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
      {confirmation && (
        <ConfirmDialog
          busy={busy}
          description={confirmation.description}
          error={confirmationError}
          title={confirmation.title}
          warning={confirmation.warning}
          onCancel={() => {
            setConfirmation(undefined);
            setConfirmationError(undefined);
          }}
          onConfirm={() => {
            const { action } = confirmation;

            void run(async () => {
              await action();
              if (mounted.current) setConfirmation(undefined);
            }, setConfirmationError);
          }}
        />
      )}
    </section>
  );
}

function ManagedServerCard({
  server,
  busy,
  onOpen,
  onForget,
  onSaveMappings,
}: {
  server: ManagedServer;
  busy: boolean;
  /** Shows the server in this window, on one of its own routes when given one. */
  onOpen: (route?: string) => void;
  onForget: () => void;
  onSaveMappings: (mappings: ManagedServerPathMapping[]) => Promise<boolean>;
}) {
  const { t } = useTranslation();
  const [mappings, setMappings] = useState(server.pathMappings);
  const [dirty, setDirty] = useState(false);
  const name = serverLabel(server);

  useEffect(() => {
    if (!dirty) setMappings(server.pathMappings);
  }, [server.pathMappings, dirty]);

  const edit = (next: (rows: ManagedServerPathMapping[]) => ManagedServerPathMapping[]) => {
    setDirty(true);
    setMappings(next);
  };
  const save = async () => {
    // Sent whole rather than merged: a removed row has to stop mapping.
    const proposed = mappings.map((row) => ({
      serverPath: row.serverPath.trim(),
      localPath: row.localPath.trim(),
    }));

    if (await onSaveMappings(proposed)) {
      setMappings(proposed);
      setDirty(false);
    }
  };

  return (
    <article
      aria-label={name}
      className="space-y-3 rounded-lg border border-default-200 p-3"
      data-testid="managed-server"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-semibold">{name}</h3>
          <p className="mt-1 break-all text-xs text-default-500">
            {server.address}
            {server.appVersion ? ` · v${server.appVersion}` : ""}
          </p>
          {server.importedFromLegacyClient && (
            <p className="mt-1 text-xs text-default-400">{t("federation.servers.imported")}</p>
          )}
        </div>
        <span className={`rounded-md px-2 py-1 text-xs ${stateBadgeClass[server.state] ?? ""}`}>
          {t(`federation.servers.state.${server.state}`)}
        </span>
      </div>
      {server.mode === RemoteAccessMode.Unrestricted && (
        // Warned about, never changed from here: only the other device's owner decides.
        // The way there is the server's own Configuration page — its devices page belongs
        // to this computer and is not in the menu while the window shows another server.
        // The labels are the settings page's own, so the directions name what it shows.
        <div
          className="space-y-2 rounded-lg border border-warning/40 bg-warning/10 p-2 text-xs"
          data-testid="managed-server-unrestricted"
        >
          <p>
            {t("federation.servers.unrestricted", {
              name,
              page: t("menu.configuration"),
              setting: t("configuration.remoteAccess.mode.label"),
              enabled: t("configuration.remoteAccess.mode.enabled"),
              pairing: t("configuration.remoteAccess.requirePairing.label"),
            })}
          </p>
          <button
            className={buttonClass}
            disabled={busy}
            type="button"
            onClick={() => onOpen(CONFIGURATION_ROUTE)}
          >
            {t("federation.servers.openConfiguration", { page: t("menu.configuration") })}
          </button>
        </div>
      )}
      {server.state === ManagedServerState.Revoked && (
        <p className="text-xs text-danger">{t("federation.servers.revokedTip", { name })}</p>
      )}
      {server.state === ManagedServerState.WrongServer && (
        // Never "pair again here": whoever answers at the address is not this server, and
        // pairing with it is exactly the mistake the state is there to prevent.
        <p
          className="text-xs text-warning-600 dark:text-warning"
          data-testid="managed-server-wrong-server"
        >
          {server.answeredBy?.isThisDevice
            ? t("federation.servers.wrongServerThisDeviceTip", {
                name,
                address: server.address,
                discover: t("federation.servers.add.discover"),
              })
            : t("federation.servers.wrongServerTip", {
                name,
                address: server.address,
                other: server.answeredBy?.name || server.answeredBy?.serverId || "?",
                discover: t("federation.servers.add.discover"),
              })}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <button className={primaryClass} disabled={busy} type="button" onClick={() => onOpen()}>
          {t("federation.servers.open")}
        </button>
        <button
          className={`${buttonClass} text-danger`}
          disabled={busy}
          type="button"
          onClick={onForget}
        >
          {t("federation.servers.forget")}
        </button>
      </div>
      <details className="border-t border-default-200 pt-3">
        <summary className="cursor-pointer text-sm font-medium">
          {t("federation.servers.mappings.title")}
          {server.pathMappings.length > 0 && (
            <span className="ml-2 text-xs text-default-500">{server.pathMappings.length}</span>
          )}
        </summary>
        <p className="mt-2 text-xs text-default-500">{t("federation.servers.mappings.tip")}</p>
        <div className="mt-3 space-y-2">
          {mappings.map((mapping, index) => (
            <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
              <label className="space-y-1 text-xs">
                <span>{t("federation.servers.mappings.server")}</span>
                <input
                  className={fieldClass}
                  disabled={busy}
                  placeholder={"D:\\Media"}
                  value={mapping.serverPath}
                  onChange={(event) =>
                    edit((rows) =>
                      rows.map((row, i) =>
                        i === index ? { ...row, serverPath: event.target.value } : row,
                      ),
                    )
                  }
                />
              </label>
              <label className="space-y-1 text-xs">
                <span>{t("federation.servers.mappings.local")}</span>
                <input
                  className={fieldClass}
                  disabled={busy}
                  placeholder="/Volumes/Media"
                  value={mapping.localPath}
                  onChange={(event) =>
                    edit((rows) =>
                      rows.map((row, i) =>
                        i === index ? { ...row, localPath: event.target.value } : row,
                      ),
                    )
                  }
                />
              </label>
              <button
                aria-label={t("federation.servers.mappings.remove")}
                className={`${buttonClass} self-end`}
                disabled={busy}
                type="button"
                onClick={() => edit((rows) => rows.filter((_, i) => i !== index))}
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
            onClick={() => edit((rows) => [...rows, { serverPath: "", localPath: "" }])}
          >
            {t("federation.servers.mappings.add")}
          </button>
          <button
            className={primaryClass}
            disabled={
              busy ||
              !dirty ||
              mappings.some((row) => !row.serverPath.trim() || !row.localPath.trim())
            }
            type="button"
            onClick={() => void save()}
          >
            {t("federation.servers.mappings.save")}
          </button>
        </div>
      </details>
    </article>
  );
}
