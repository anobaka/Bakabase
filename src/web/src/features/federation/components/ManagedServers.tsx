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

import { useFocusOnOpen } from "../hooks/useFocusOnOpen";
import { revealClass } from "../hooks/useSectionReveal";
import { managedServerApi } from "../serverApi";
import { CONFIGURATION_ROUTE, openManagedServer } from "../switching";
import { FederationError, isAbort } from "../transport";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  fieldClass,
  focusHeadingClass,
  panelClass,
  primaryClass,
} from "./common";
import ConfirmDialog from "./ConfirmDialog";
import ManagedServerPathMappings from "./ManagedServerPathMappings";

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

export const stateBadgeClass: Record<ManagedServerState, string> = {
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

/** Where the list of managed servers comes from: the page's own reads, or the section's. */
export interface ManagedServersSource {
  view?: ManagedServersView;
  error?: Error;
  loading: boolean;
  /** `probe` also asks each server how it is; `quiet` keeps the last listing on a failure. */
  load: (options?: { probe?: boolean; quiet?: boolean }) => Promise<void>;
}

/**
 * The listing read by the section itself, where no page reads it for it: a plain listing
 * first so it fills at once, then a probed one for states, and re-read while a request
 * waits for its answer.
 */
function useOwnManagedServers(onSettled?: () => void): ManagedServersSource {
  const [view, setView] = useState<ManagedServersView>();
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(false);
  const mounted = useRef(true);
  const generation = useRef(0);
  const latestSettled = useRef(onSettled);
  const settled = useRef(false);

  latestSettled.current = onSettled;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      generation.current += 1;
    };
  }, []);

  /**
   * `quiet` is for polling and the follow-up probe: no loading state, and a failure keeps
   * the last good listing instead of replacing it with an error.
   */
  const load = useCallback(async (options: { probe?: boolean; quiet?: boolean } = {}) => {
    const run = ++generation.current;

    if (!options.quiet) setLoading(true);
    try {
      const fresh = await managedServerApi.list(options.probe === true);

      if (run !== generation.current) return;
      setView(fresh);
      setError(undefined);
    } catch (cause) {
      if (run === generation.current && !options.quiet)
        setError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (run === generation.current && !options.quiet) setLoading(false);
      if (!settled.current && mounted.current) {
        settled.current = true;
        latestSettled.current?.();
      }
    }
  }, []);

  useEffect(() => {
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
      if (!document.hidden) void load({ quiet: true });
    }, REQUEST_POLL_MS);

    return () => clearInterval(timer);
  }, [waiting, load]);

  return { view, error, loading, load };
}

export interface ManagedServersProps {
  /** The section element, for a page that brings it into view. */
  sectionRef?: Ref<HTMLElement>;
  /** Marks the section for a moment after a link led here. */
  highlighted?: boolean;
  /** The section's heading level: 2 on its own, 3 inside the devices page's tab. */
  headingLevel?: 2 | 3;
  /**
   * With servers listed, the add form waits behind a button; `addRequested` (a link that
   * asked for it) opens it, and so does a server that has to be found or added again (its
   * card's tip points at the search in the form). Without this the form is always open.
   */
  collapseAdd?: boolean;
  addRequested?: boolean;
  /** Marks the add form for a moment after a link led to it. */
  addHighlighted?: boolean;
}

/**
 * Other devices this one manages in full — the removed thin client's job, now done by
 * switching this window to the other device's own UI. Reads its own listing; the devices
 * page passes the one it reads for the whole page to {@link ManagedServersPanel} instead.
 */
export default function ManagedServersSection({
  onSettled,
  ...props
}: ManagedServersProps & {
  /** Called once, when the first listing has finished — either way. */
  onSettled?: () => void;
}) {
  const source = useOwnManagedServers(onSettled);

  return <ManagedServersPanel {...props} source={source} />;
}

/**
 * Kept apart from the read-only library sharing on purpose: this is the other device's
 * administrator access, with its own pairing, and nothing here grants or uses a sharing
 * permission.
 */
export function ManagedServersPanel({
  source,
  sectionRef,
  highlighted = false,
  headingLevel = 2,
  collapseAdd = false,
  addRequested = false,
  addHighlighted = false,
}: ManagedServersProps & { source: ManagedServersSource }) {
  const { t } = useTranslation();
  const { view, error: loadError, loading, load } = source;
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const mounted = useRef(true);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState<string>();
  const [address, setAddress] = useState("");
  const [code, setCode] = useState("");
  const [addOpened, setAddOpened] = useState(false);
  const addFocus = useFocusOnOpen<HTMLInputElement>(addOpened);
  const [discovered, setDiscovered] = useState<ManagedServerCandidate[]>();
  const [discovering, setDiscovering] = useState(false);
  const [discoverError, setDiscoverError] = useState<Error>();
  /** The search under way, so leaving the page can stop listening for its answer. */
  const discovery = useRef<AbortController>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [confirmationError, setConfirmationError] = useState<Error>();
  /**
   * What the last listing held, so an answer arriving between two reads can be named.
   * Only live requests: one that already shows how it ended is not news when it goes.
   */
  const previous = useRef<{ servers: Set<string>; live: Map<string, string> }>();
  /** Requests this page withdrew itself; their disappearance is not news. */
  const withdrawn = useRef(new Set<string>());
  const latestT = useRef(t);
  const Heading = headingLevel === 3 ? "h3" : "h2";
  const SubHeading = headingLevel === 3 ? "h4" : "h3";

  latestT.current = t;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      discovery.current?.abort();
    };
  }, []);

  // Only a new listing is news.
  useEffect(() => {
    if (!view) return;
    const before = previous.current;

    if (before) {
      const added = view.servers.filter((server) => !before.servers.has(server.serverId));
      // Requests that were live and now are not listed at all. One that ended with an
      // answer stays listed for a while and says so in its own row; one that is gone was
      // either approved — its server is new above — or ended somewhere this page cannot see.
      const gone = [...before.live.entries()].filter(
        ([requestId]) =>
          !withdrawn.current.has(requestId) &&
          !view.requests.some((request) => request.requestId === requestId),
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
      servers: new Set(view.servers.map((server) => server.serverId)),
      live: new Map(
        view.requests
          .filter((request) => request.active)
          .map((request) => [request.requestId, requestLabel(request)]),
      ),
    };
  }, [view]);

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
  // A server whose address answers as another, or that revoked this device, is found and
  // added again with the form: its tip names the search in it.
  const needsAdding = servers.some(
    (server) =>
      server.state === ManagedServerState.WrongServer ||
      server.state === ManagedServerState.Revoked,
  );
  const addOpen = !collapseAdd || !servers.length || addOpened || addRequested || needsAdding;

  return (
    <section
      ref={sectionRef}
      data-focus-section
      aria-busy={loading || undefined}
      aria-labelledby="managed-servers-title"
      className={`${panelClass} space-y-4 ${revealClass(highlighted)}`}
      data-highlighted={highlighted || undefined}
      id="managed-servers"
      tabIndex={-1}
    >
      <Heading
        className={`flex items-center gap-2 font-semibold ${focusHeadingClass}`}
        id="managed-servers-title"
        tabIndex={-1}
      >
        <AiOutlineCloudServer aria-hidden />
        {t("federation.servers.title")}
      </Heading>
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
              headingLevel={SubHeading}
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
      {/* A place a link lands on (`add-server`), named by the form's heading once open. */}
      <div
        aria-labelledby={addOpen ? "managed-server-add-title" : undefined}
        className={`border-t border-default-200 pt-4 ${revealClass(addHighlighted)}`}
        data-highlighted={addHighlighted || undefined}
        id="managed-server-add"
        role={addOpen ? "group" : undefined}
        tabIndex={-1}
      >
        {!addOpen ? (
          <button
            aria-expanded={false}
            className={buttonClass}
            type="button"
            onClick={() => {
              addFocus.request();
              setAddOpened(true);
            }}
          >
            <AiOutlinePlus aria-hidden />
            {t("federation.servers.add.title")}
          </button>
        ) : (
          <>
            <SubHeading className="font-medium" id="managed-server-add-title">
              {t("federation.servers.add.title")}
            </SubHeading>
            <p className="mt-1 text-sm text-default-500">
              {t("federation.servers.add.description")}
            </p>
            <form
              className="mt-3 grid items-end gap-3 @3xl:grid-cols-[1fr_200px_auto]"
              onSubmit={(event) => {
                event.preventDefault();
                const target = address.trim();

                if (target) void pair(target, code.trim() || undefined);
              }}
            >
              <label className="space-y-1 text-sm">
                <span>{t("federation.servers.add.address")}</span>
                <input
                  ref={addFocus.target}
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
                {t(
                  code.trim()
                    ? "federation.servers.add.withCode"
                    : "federation.servers.add.request",
                )}
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
                discovering
                  ? "federation.servers.add.discovering"
                  : "federation.servers.add.discover",
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
                  <p className="text-sm text-default-500">
                    {t("federation.servers.add.noneFound")}
                  </p>
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
                        // Found, and said so — a search that silently left it out would read
                        // as "not on this network" — but not offered again: it is listed above.
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
          </>
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
  headingLevel: Title = "h3",
  onOpen,
  onForget,
  onSaveMappings,
}: {
  server: ManagedServer;
  busy: boolean;
  headingLevel?: "h3" | "h4";
  /** Shows the server in this window, on one of its own routes when given one. */
  onOpen: (route?: string) => void;
  onForget: () => void;
  onSaveMappings: (mappings: ManagedServerPathMapping[]) => Promise<boolean>;
}) {
  const { t } = useTranslation();
  const name = serverLabel(server);

  return (
    <article
      aria-label={name}
      className="space-y-3 rounded-lg border border-default-200 p-3"
      data-testid="managed-server"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <Title className="font-semibold">{name}</Title>
          <p className="mt-1 break-all text-xs text-default-500">
            {server.address}
            {server.appVersion ? ` · v${server.appVersion}` : ""}
          </p>
        </div>
        <span className={`rounded-md px-2 py-1 text-xs ${stateBadgeClass[server.state] ?? ""}`}>
          {t(`federation.servers.state.${server.state}`)}
        </span>
      </div>
      <ManagedServerWarnings busy={busy} name={name} server={server} onOpen={onOpen} />
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
      <ManagedServerPathMappings busy={busy} server={server} onSave={onSaveMappings} />
    </article>
  );
}

/** How to find a server whose address answers as another, where the warning is shown. */
const tips = {
  devices: {
    other: "federation.servers.wrongServerTip",
    thisDevice: "federation.servers.wrongServerThisDeviceTip",
    sameIdentity: "federation.servers.wrongServerSameIdentityTip",
  },
  map: {
    other: "federation.map.panel.wrongServerTip",
    thisDevice: "federation.map.panel.wrongServerThisDeviceTip",
    sameIdentity: "federation.map.panel.wrongServerSameIdentityTip",
  },
} as const;

/**
 * What the reader should know about a managed server before using it: it lets anyone on its
 * network manage it, it revoked this device, or its address now answers as another server.
 * Shown on its card here and in its panel on the device map.
 */
export function ManagedServerWarnings({
  server,
  name,
  busy,
  onOpen,
  where = "devices",
}: {
  server: ManagedServer;
  name: string;
  busy: boolean;
  /** Shows the server in this window, on one of its own routes when given one. */
  onOpen: (route?: string) => void;
  /**
   * Where it is shown, so the way back from a server that moved names what is there: the
   * devices page's search below the card, or the map's at its top and its panel.
   */
  where?: "devices" | "map";
}) {
  const { t } = useTranslation();

  return (
    <>
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
            ? t(tips[where].thisDevice, {
                name,
                address: server.address,
                discover: t("federation.servers.add.discover"),
              })
            : t(tips[where][server.answeredBy?.isSameIdentity ? "sameIdentity" : "other"], {
                name,
                address: server.address,
                other: server.answeredBy?.name || server.answeredBy?.serverId || "?",
                discover: t("federation.servers.add.discover"),
              })}
        </p>
      )}
    </>
  );
}
