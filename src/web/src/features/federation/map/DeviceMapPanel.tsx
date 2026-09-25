import type { MutableRefObject, ReactNode, Ref } from "react";
import type {
  BakabaseServiceModelsViewRemoteAccessDeviceViewModel as PairedDevice,
  BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel as ManagementRequest,
  BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings,
} from "@/sdk/Api";
import type {
  FederationStatus,
  ManagedServer,
  ManagedServerPairing,
  PairingRequest,
} from "../types";
import type { DeviceGraph, MapEdge, MapEdgeKind, MapNode } from "./graph";
import type { MapSelection } from "./DeviceMapCanvas";
import type { MapSource } from "./useDeviceMapData";
import type { NoticeState, PanelActions } from "./usePanelActions";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineClose } from "react-icons/ai";

import {
  buttonClass,
  DismissButton,
  ErrorNotice,
  fieldClass,
  MessageError,
  primaryClass,
} from "../components/common";
import ConfirmDialog from "../components/ConfirmDialog";
import ManagedServerPathMappings from "../components/ManagedServerPathMappings";
import { ManagedServerWarnings, stateBadgeClass } from "../components/ManagedServers";
import PeerPathMappings from "../components/PeerPathMappings";
import { federationPeerApi } from "../peerApi";
import { managedServerApi } from "../serverApi";
import { devicesRoute, openManagedServer } from "../switching";
import { FederationError } from "../transport";

import {
  SELF_ID,
  canManageFromHere,
  edgesOf,
  identityKey,
  ownHostsOf,
  sameMachine,
  withScheme,
} from "./graph";
import { edgeStyles, KindBadge, KindGlyph } from "./DeviceMapCanvas";
import { edgeKindLabel, issueLabel, namesakeDescription, nodeName, nodeSummary } from "./describe";
import { usePanelActions } from "./usePanelActions";
import { useEscapeKey } from "./useEscapeKey";

import BApi from "@/sdk/BApi";
import {
  ManagedServerOutcome,
  ManagedServerOutcomeLabel,
  ManagedServerState,
  RemoteAccessMode,
} from "@/sdk/constants";
import { millisecondsUntil, minutesUntil, parseServerTime } from "@/core/serverTime";
import { remoteDevicePlatformLabelKey } from "@/core/remoteDevicePlatform";

/** Failures show in the panel; a toast from the shared client would say it twice. */
const inline = { showErrorToast: false } as const;

/** The multi-device library, searching only the given device (and choosing it). */
export const libraryRoute = (nodeId: string) =>
  `/federation?scope=selected&source=${encodeURIComponent(nodeId)}`;

const sectionClass = "space-y-3 border-t border-default-200 pt-4";

interface PanelContext {
  graph: DeviceGraph;
  status?: FederationStatus;
  access?: RemoteAccessSettings;
  actions: PanelActions;
  /**
   * Names the record an action just turned this device into, so the panel stays with it —
   * and, when that record only shows up later, where it will (see `onFollow`).
   */
  follow: (keys: (string | undefined | null | false)[], wait?: MapSource[]) => void;
  /** The hosts this device answers on, which are this machine like `localhost` is. */
  ownHosts: ReadonlySet<string>;
  onSelect: (selection: MapSelection) => void;
}

/** A pairing outcome the server reported rather than threw. */
const outcomeError = (outcome: ManagedServerOutcome, detail?: string | null) =>
  new FederationError(
    `ManagedServer${ManagedServerOutcomeLabel[outcome] ?? outcome}`,
    detail ?? "",
    0,
  );

export interface DeviceMapPanelProps {
  graph: DeviceGraph;
  selection: MapSelection;
  status?: FederationStatus;
  access?: RemoteAccessSettings;
  headingRef?: Ref<HTMLHeadingElement>;
  onSelect: (selection: MapSelection) => void;
  onClose: () => void;
  onChanged: (sources: MapSource[]) => Promise<unknown> | void;
  /**
   * Nothing is selected: the panel shows this device as the map's starting point, with a
   * word on how to use the map.
   */
  overview?: boolean;
  /** Whether it can be closed (with its button or Escape): by default, unless an overview. */
  closable?: boolean;
  /** What the last action said, held by the page so it outlives the record it was about. */
  notice?: NoticeState;
  /**
   * An action turned this device's record into another: the identity keys that record will
   * carry, most specific first, for the page to follow the selection to it — and, for one
   * that appears only later, the listings to re-read until it does.
   */
  onFollow?: (keys: string[], wait?: MapSource[]) => void;
  /** An action begins: before its button is disabled while it runs. */
  onActionStart?: () => void;
  /** It is over, listings re-read — even when this panel is gone by then. */
  onActionEnd?: () => void;
}

/**
 * Details of what is selected on the map, with the actions the devices page offers for it —
 * the same endpoints, the same confirmations, the same words. Destructive actions are
 * confirmed first; nothing here changes another device's settings.
 */
export default function DeviceMapPanel({
  graph,
  selection,
  status,
  access,
  headingRef,
  onSelect,
  onClose,
  onChanged,
  overview = false,
  closable = !overview,
  notice,
  onFollow,
  onActionStart,
  onActionEnd,
}: DeviceMapPanelProps) {
  const { t } = useTranslation();
  const actions = usePanelActions(onChanged, notice, {
    onStart: onActionStart,
    onEnd: onActionEnd,
  });
  const follow = (keys: (string | undefined | null | false)[], wait?: MapSource[]) =>
    onFollow?.(
      keys.filter((key): key is string => !!key),
      wait,
    );
  const ownHosts = useMemo(() => ownHostsOf(status, access), [status, access]);
  const context: PanelContext = { graph, status, access, actions, follow, ownHosts, onSelect };
  const node =
    selection.type === "node"
      ? selection.id === SELF_ID
        ? graph.self
        : graph.nodes.find((item) => item.id === selection.id)
      : graph.nodes.find(
          (item) => item.id === graph.edges.find((edge) => edge.id === selection.id)?.nodeId,
        );
  const edge =
    selection.type === "edge" ? graph.edges.find((item) => item.id === selection.id) : undefined;
  const selectionKey = `${selection.type}:${selection.id}`;
  const region = useRef<HTMLElement>(null);
  const title = useRef<HTMLHeadingElement | null>(null);
  const messages = useRef<HTMLDivElement>(null);
  const setTitle = useCallback(
    (element: HTMLHeadingElement | null) => {
      title.current = element;
      if (typeof headingRef === "function") headingRef(element);
      else if (headingRef)
        (headingRef as MutableRefObject<HTMLHeadingElement | null>).current = element;
    },
    [headingRef],
  );

  // Escape leaves the details — unless a confirmation is open, which Escape cancels instead.
  useEscapeKey(region, onClose, closable && !actions.confirmation);

  if (!node) return null;
  const name = nodeName(t, node);
  /**
   * A message's × takes itself away with the message: the keyboard that pressed it goes to the
   * details' heading rather than to the page's body. Focus the reader put elsewhere stays there.
   */
  const dismiss = (clear: () => void) => () => {
    const active = document.activeElement;
    const fromMessage = !active || active === document.body || !!messages.current?.contains(active);

    clear();
    if (fromMessage) title.current?.focus();
  };

  return (
    <section
      ref={region}
      aria-labelledby="device-map-panel-title"
      className="space-y-4"
      data-overview={overview || undefined}
      data-selection={selectionKey}
      data-testid="device-map-panel"
    >
      <header className="flex items-start gap-3">
        <svg aria-hidden className="mt-0.5 h-9 w-9 shrink-0" viewBox="-18 -18 36 36">
          <rect
            className={
              node.self ? "fill-primary-50 stroke-primary" : "fill-default-100 stroke-default-300"
            }
            height={34}
            rx={8}
            strokeDasharray={node.ghost ? "4 3" : undefined}
            width={34}
            x={-17}
            y={-17}
          />
          <KindGlyph
            className={node.self ? "stroke-primary" : "stroke-default-600"}
            kind={node.kind}
          />
        </svg>
        <div className="min-w-0 flex-1">
          {edge && (
            <p className={`text-xs font-medium ${edgeStyles[edge.kind].text}`}>
              {edgeKindLabel(t, edge)}
            </p>
          )}
          <h2
            ref={setTitle}
            className="break-words text-lg font-semibold outline-none"
            id="device-map-panel-title"
            tabIndex={-1}
          >
            {name}
          </h2>
          <p className="text-xs text-default-500">{nodeSummary(t, node)}</p>
          {(node.address || node.appVersion) && !node.self && (
            <p className="mt-1 break-all text-xs text-default-400">
              {node.address}
              {node.appVersion ? `${node.address ? " · " : ""}v${node.appVersion}` : ""}
            </p>
          )}
        </div>
        {closable && (
          <button
            aria-label={t("federation.close")}
            className="-m-1 rounded p-1 text-default-500 hover:bg-default-100"
            type="button"
            onClick={onClose}
          >
            <AiOutlineClose aria-hidden />
          </button>
        )}
      </header>
      {overview && (
        <p
          className="rounded-lg bg-default-50 p-2 text-xs text-default-500"
          data-testid="device-map-hint"
        >
          {t("federation.map.hint")}
        </p>
      )}

      {node.unverified && <UnverifiedNote node={node} onSelect={onSelect} />}
      {node.namesakes.length > 0 && <NamesakeNote graph={graph} node={node} onSelect={onSelect} />}
      {node.issues.length > 0 && (
        <ul className="flex flex-wrap gap-1.5" data-testid="device-map-issues">
          {node.issues.map((issue) => (
            <li
              key={issue}
              className="rounded-md bg-warning/10 px-2 py-0.5 text-xs text-warning-700 dark:text-warning"
            >
              {issueLabel(t, issue)}
            </li>
          ))}
        </ul>
      )}

      {(actions.error || actions.notice) && (
        <div ref={messages} className="space-y-2">
          <ErrorNotice
            error={actions.error}
            onDismiss={dismiss(() => actions.setError(undefined))}
          />
          {actions.notice && (
            <div
              className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
              role="status"
            >
              <p>{actions.notice}</p>
              <DismissButton onClick={dismiss(() => actions.setNotice(undefined))} />
            </div>
          )}
        </div>
      )}

      {node.self ? (
        <SelfDetails context={context} />
      ) : node.ghost ? (
        <GhostDetails context={context} node={node} />
      ) : (
        <>
          {edge && (
            <button
              className="text-xs text-primary underline"
              type="button"
              onClick={() => onSelect({ type: "node", id: node.id })}
            >
              {t("federation.map.panel.showDevice", { name })}
            </button>
          )}
          {(!edge || edge.kind === "sharing") && <SharingSection context={context} node={node} />}
          {(!edge || edge.kind === "management") && (
            <ManagementSection context={context} node={node} />
          )}
        </>
      )}

      {actions.confirmation && (
        <ConfirmDialog
          busy={actions.busy}
          description={actions.confirmation.description}
          error={actions.confirmationError}
          title={actions.confirmation.title}
          warning={actions.confirmation.warning}
          onCancel={actions.cancelConfirmation}
          onConfirm={actions.confirmCurrent}
        />
      )}
    </section>
  );
}

/**
 * A device known only by its own request: its name and identity are its claims. When it
 * claims to be a device this one knows, where that device is known is set against where the
 * request came from — the one thing here nobody could make up.
 */
function UnverifiedNote({
  node,
  onSelect,
}: {
  node: MapNode;
  onSelect: (selection: MapSelection) => void;
}) {
  const { t } = useTranslation();
  const claim = node.claimsToBe;
  const from =
    node.sources.sharingRequests.find((request) => request.remoteAddress)?.remoteAddress ??
    node.sources.managementRequestsIn.find((request) => request.remoteAddress)?.remoteAddress ??
    t("federation.map.panel.claim.unknownFrom");

  return (
    <div
      className="space-y-1.5 rounded-lg bg-warning/10 p-2 text-xs text-warning-700 dark:text-warning"
      data-testid="device-map-unverified"
    >
      <p>{t("federation.map.panel.unverified")}</p>
      {claim && (
        <p data-testid="device-map-claim">
          {claim.address
            ? t("federation.map.panel.claim.knownAt", {
                name: claim.name,
                known: claim.address,
                from,
              })
            : t("federation.map.panel.claim.known", { name: claim.name, from })}{" "}
          <button
            className="underline"
            type="button"
            onClick={() => onSelect({ type: "node", id: claim.nodeId })}
          >
            {t("federation.map.panel.showDevice", { name: claim.name })}
          </button>
        </p>
      )}
    </div>
  );
}

/**
 * Other devices of this one's name that nothing else ties to it — an install id tells them
 * apart, or the name is all there is: it may be either, which the reader is told on both and
 * can look at, never merged. Since the name is what they share, each is named by what it is to
 * this device and where it is (`namesakeDescription`), in its text and so in its button's
 * accessible name: three records of one name never read as three identical rows.
 */
function NamesakeNote({
  graph,
  node,
  onSelect,
}: {
  graph: DeviceGraph;
  node: MapNode;
  onSelect: (selection: MapSelection) => void;
}) {
  const { t } = useTranslation();
  // Each by what it is to this device and where it is, since the name is what they share —
  // and, where two still read alike, when each was paired.
  const others = node.namesakes.map((other) => {
    const record = graph.nodes.find((item) => item.id === other.nodeId);

    return { ...other, record, what: record ? namesakeDescription(t, record) : undefined };
  });
  const alike = (what?: string) => others.filter((other) => other.what === what).length > 1;

  return (
    <div
      className="space-y-1.5 rounded-lg bg-default-100 p-2 text-xs text-default-600"
      data-testid="device-map-namesakes"
    >
      <p>
        {others.length === 1
          ? t("federation.map.panel.namesakes.one")
          : t("federation.map.panel.namesakes.many", { count: others.length })}
      </p>
      <ul className="space-y-1">
        {others.map((other) => {
          const what =
            other.record && alike(other.what)
              ? namesakeDescription(t, other.record, true)
              : other.what;

          return (
            <li key={other.nodeId} data-namesake={other.nodeId}>
              <button
                className="text-left underline"
                type="button"
                onClick={() => onSelect({ type: "node", id: other.nodeId })}
              >
                {what
                  ? t("federation.map.panel.showNamesake", { name: other.name, what })
                  : t("federation.map.panel.showDevice", { name: other.name })}
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/** One direction of a relationship, as a sentence with its state and what can be done. */
function DirectionRow({
  kind,
  edge,
  direction,
  name,
  children,
  testId,
}: {
  kind: MapEdgeKind;
  edge?: MapEdge;
  direction: "in" | "out";
  name: string;
  children?: ReactNode;
  testId: string;
}) {
  const { t } = useTranslation();
  const status = edge?.[direction] ?? "none";

  if (status === "none") return null;
  const style = edgeStyles[kind];

  return (
    <div
      className="space-y-2 rounded-lg border border-default-200 p-3"
      data-direction={direction}
      data-status={status}
      data-testid={testId}
    >
      <div className="flex items-start gap-2 text-sm">
        <svg aria-hidden className="mt-1 h-3 w-7 shrink-0" viewBox="0 0 28 12">
          <path
            className={style.stroke}
            d={direction === "out" ? "M2 6 H22" : "M26 6 H6"}
            strokeDasharray={status === "pending" ? "4 3" : undefined}
            strokeWidth={2}
          />
          <path
            className={style.fill}
            d={direction === "out" ? "M20 2 L27 6 L20 10 z" : "M8 2 L1 6 L8 10 z"}
          />
        </svg>
        <p className="min-w-0 flex-1">
          {t(`federation.map.direction.${kind}.${direction}.${status}`, { name })}
        </p>
      </div>
      {children}
    </div>
  );
}

const ensureOk = <T extends { code?: number; message?: string | null }>(
  rsp: T,
  fallback: string,
) => {
  if (rsp?.code) throw new MessageError(rsp.message || fallback);

  return rsp;
};

/** Read-only library sharing between this device and another, both ways. */
function SharingSection({ context, node }: { context: PanelContext; node: MapNode }) {
  const { t } = useTranslation();
  const { graph, status, actions } = context;
  const { busy, run, confirm } = actions;
  const edge = edgesOf(graph, node.id).find((item) => item.kind === "sharing");
  const peer = node.sources.peer;
  const name = nodeName(t, node);
  const now = Date.now();
  const incoming = node.sources.sharingRequests.filter((r) => r.direction === "incoming");
  const outgoing = node.sources.sharingRequests.filter((r) => r.direction === "outgoing");
  const canRequest =
    !peer?.outboundGrant &&
    !outgoing.length &&
    !!node.address &&
    !node.issues.includes("wrongServer");
  const [invite, setInvite] = useState<{ code: string; expiresAt: string }>();

  const approve = (request: PairingRequest) => {
    const reciprocal = request.offersReciprocalAccess;

    confirm({
      title: t("federation.requests.approve"),
      description: t(
        request.remoteAddress
          ? reciprocal
            ? "federation.requests.approveConfirmFromReciprocal"
            : "federation.requests.approveConfirmFrom"
          : reciprocal
            ? "federation.requests.approveConfirmReciprocal"
            : "federation.requests.approveConfirm",
        { name: request.nodeName, address: request.remoteAddress },
      ),
      warning: request.replacesExistingAccess
        ? t("federation.requests.replacesExisting")
        : undefined,
      action: async () => {
        await federationPeerApi.decide(request.requestId, true);
        // Approved, the claimed id is a device this one shares with: the panel moves there.
        context.follow([identityKey.install(request.nodeId)]);
      },
      refresh: ["sharing"],
    });
  };

  return (
    <section
      aria-labelledby={`sharing-${node.id}`}
      className={sectionClass}
      data-testid="device-map-sharing"
    >
      <h3
        className={`flex items-center gap-2 text-sm font-semibold ${edgeStyles.sharing.text}`}
        id={`sharing-${node.id}`}
      >
        <KindBadge kind="sharing" />
        {t("federation.map.edge.sharing")}
      </h3>
      {!edge && (
        <p className="text-sm text-default-500">
          {t("federation.map.panel.sharing.none", { name })}
        </p>
      )}

      <DirectionRow direction="in" edge={edge} kind="sharing" name={name} testId="sharing-in">
        {peer?.outboundGrant ? (
          <>
            {peer.connectionState !== "Online" && peer.connectionState !== "Unknown" && (
              <p className="text-xs text-default-500">
                {t(`federation.connection.${peer.connectionState}`, {
                  defaultValue: peer.connectionState,
                })}
              </p>
            )}
            <div className="flex flex-wrap items-center gap-2">
              <Link className={primaryClass} to={libraryRoute(peer.nodeId)}>
                {t("federation.map.panel.openLibrary")}
              </Link>
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  confirm({
                    title: t("federation.devices.forget"),
                    description: t("federation.devices.forgetConfirm", { name }),
                    action: () => federationPeerApi.forget(peer.nodeId),
                    refresh: ["sharing"],
                  })
                }
              >
                {t("federation.devices.forget")}
              </button>
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input
                checked={peer.enabled}
                disabled={busy}
                type="checkbox"
                onChange={(event) =>
                  void run(
                    () => federationPeerApi.enable(peer.nodeId, event.target.checked),
                    ["sharing"],
                  )
                }
              />
              {t("federation.devices.include")}
            </label>
            <PeerPathMappings
              busy={busy}
              className="pt-1"
              peer={peer}
              onSave={(mappings, expected) =>
                run(async () => {
                  await federationPeerApi.mappings(peer.nodeId, mappings, expected);
                  if (actions.mounted.current) actions.setNotice(t("federation.mappings.saved"));
                }, ["sharing"])
              }
            />
          </>
        ) : (
          <>
            {incoming.some((request) => request.offersReciprocalAccess) && (
              // Asked for by the other device: approving its request below opens this way too.
              <p className="text-xs text-success">
                {t("federation.requests.offersReciprocal", { name })}
              </p>
            )}
            {outgoing.map((request) => (
              <div key={request.requestId} className="flex flex-wrap items-center gap-2">
                <p className="mr-auto text-xs text-default-500">
                  {t("federation.expires", {
                    time: parseServerTime(request.expiresAt)?.toLocaleTimeString() ?? "",
                  })}
                </p>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(async () => {
                      const result = await federationPeerApi.claim(request.requestId);

                      if (actions.mounted.current)
                        actions.setNotice(t(`federation.pair.${result.outcome}`));
                    }, ["sharing"])
                  }
                >
                  {t("federation.requests.check")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(() => federationPeerApi.cancelRequest(request.requestId), ["sharing"])
                  }
                >
                  {t("federation.requests.cancel")}
                </button>
              </div>
            ))}
          </>
        )}
      </DirectionRow>

      <DirectionRow direction="out" edge={edge} kind="sharing" name={name} testId="sharing-out">
        {peer?.inboundGrant && (
          <button
            className={`${buttonClass} text-danger`}
            disabled={busy}
            type="button"
            onClick={() =>
              confirm({
                title: t("federation.devices.revoke"),
                description: t("federation.devices.revokeConfirm", { name }),
                action: () => federationPeerApi.revoke(peer.inboundGrant!.grantId),
                refresh: ["sharing"],
              })
            }
          >
            {t("federation.devices.revoke")}
          </button>
        )}
        {
          // Also beside a grant it already has: a new request would replace that access,
          // which its approval says.
          incoming.map((request) => (
            <div key={request.requestId} className="space-y-2">
              {request.remoteAddress && (
                <p className="break-all text-xs text-default-500">
                  {t("federation.requests.from", { address: request.remoteAddress })}
                </p>
              )}
              {request.offersReciprocalAccess && (
                <p className="text-xs text-success">
                  {t("federation.requests.offersReciprocal", { name: request.nodeName })}
                </p>
              )}
              {request.replacesExistingAccess && (
                <p className="text-xs text-warning">{t("federation.requests.replacesExisting")}</p>
              )}
              <div className="flex flex-wrap gap-2">
                <button
                  className={primaryClass}
                  disabled={busy}
                  type="button"
                  onClick={() => approve(request)}
                >
                  {t("federation.requests.approve")}
                </button>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(() => federationPeerApi.decide(request.requestId, false), ["sharing"])
                  }
                >
                  {t("federation.requests.reject")}
                </button>
              </div>
            </div>
          ))
        }
      </DirectionRow>

      {canRequest && (
        <SharingRequestForm
          actions={actions}
          address={node.address!}
          follow={context.follow}
          remoteDisabled={status?.remoteAccessMode === RemoteAccessMode.Disabled}
        />
      )}

      {peer?.outboundGrant && !peer.inboundGrant && !incoming.length && (
        // Letting another device read this one is decided here and entered there: a code
        // made here, typed into that device's "Connect another device".
        <div className="space-y-2 rounded-lg bg-default-50 p-3 text-sm" data-testid="share-mine">
          <p className="font-medium">{t("federation.map.panel.shareMine.title", { name })}</p>
          <p className="text-xs text-default-500">
            {t("federation.map.panel.shareMine.tip", { name })}
          </p>
          {status?.sharingEnabled ? (
            <>
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  void run(async () => {
                    const issued = await federationPeerApi.invite();

                    if (actions.mounted.current) setInvite(issued);
                  }, [])
                }
              >
                {t("federation.sharing.issueCode")}
              </button>
              {invite && millisecondsUntil(invite.expiresAt, now) > 0 && (
                <div>
                  <code className="block text-2xl tracking-[0.25em]">{invite.code}</code>
                  <p className="text-xs text-default-500">
                    {t("federation.expires", {
                      time: parseServerTime(invite.expiresAt)?.toLocaleTimeString() ?? "",
                    })}
                  </p>
                </div>
              )}
            </>
          ) : (
            <Link className="text-xs text-primary underline" to={devicesRoute()}>
              {t("federation.map.panel.shareMine.enableFirst")}
            </Link>
          )}
        </div>
      )}

      {peer && (
        <button
          className={`${buttonClass} text-danger`}
          disabled={busy}
          type="button"
          onClick={() =>
            confirm({
              title: t("federation.devices.remove"),
              description: t("federation.devices.removeConfirm", { name: peer.label }),
              action: () => federationPeerApi.remove(peer.nodeId),
              refresh: ["sharing"],
            })
          }
        >
          {t("federation.devices.remove")}
        </button>
      )}
    </section>
  );
}

/** Asks another device for read access to its library — the devices page's form, for one device. */
function SharingRequestForm({
  actions,
  address,
  remoteDisabled,
  follow,
}: {
  actions: PanelActions;
  address: string;
  remoteDisabled: boolean;
  follow: PanelContext["follow"];
}) {
  const { t } = useTranslation();
  const [code, setCode] = useState("");
  const [shareBack, setShareBack] = useState(true);

  return (
    <form
      aria-label={t("federation.map.panel.requestAccess")}
      className="space-y-2 rounded-lg bg-default-50 p-3"
      data-testid="sharing-request-form"
      onSubmit={(event) => {
        event.preventDefault();
        void actions.run(async () => {
          const result = await federationPeerApi.connect(
            address,
            code.trim() || undefined,
            shareBack,
          );

          actions.setNotice(t(`federation.pair.${result.outcome}`));
          // A device found nearby becomes a request, or a device shared with.
          follow([
            result.requestId && identityKey.request(result.requestId),
            result.peerNodeId && identityKey.install(result.peerNodeId),
            identityKey.address(address),
          ]);
          if (actions.mounted.current && result.outcome === "granted") setCode("");
        }, ["sharing"]);
      }}
    >
      <p className="text-sm font-medium">{t("federation.map.panel.requestAccess")}</p>
      <label className="block space-y-1 text-xs">
        <span>{t("federation.pair.code")}</span>
        <input
          autoComplete="off"
          className={fieldClass}
          value={code}
          onChange={(event) => setCode(event.target.value)}
        />
      </label>
      <label className="flex items-start gap-2 text-xs">
        <input
          checked={shareBack}
          className="mt-0.5"
          type="checkbox"
          onChange={(event) => setShareBack(event.target.checked)}
        />
        <span>
          {t("federation.pair.shareBack")}
          <span className="mt-0.5 block text-default-500">
            {t(
              remoteDisabled
                ? "federation.pair.shareBackTipRemote"
                : "federation.pair.shareBackTip",
            )}
          </span>
        </span>
      </label>
      <button className={primaryClass} disabled={actions.busy} type="submit">
        {t(code.trim() ? "federation.pair.withCode" : "federation.pair.request")}
      </button>
    </form>
  );
}

/** Full control, either way: servers this device manages, devices that manage it. */
function ManagementSection({ context, node }: { context: PanelContext; node: MapNode }) {
  const { t } = useTranslation();
  const { graph, actions } = context;
  const { busy, run, confirm } = actions;
  const edge = edgesOf(graph, node.id).find((item) => item.kind === "management");
  const server = node.sources.server;
  const name = nodeName(t, node);
  const target = t("federation.management.self");
  const liveOut = node.sources.managementRequestsOut.filter((request) => request.active);
  const endedOut = node.sources.managementRequestsOut.filter((request) => !request.active);
  const canManage =
    canManageFromHere(graph) &&
    !server &&
    !liveOut.length &&
    !!node.address &&
    !node.issues.includes("wrongServer");
  // A managed server whose address answers as another, seen again as itself elsewhere — by
  // its own beacon, or where library sharing reaches the same install — and confirmed there
  // just now. Pairing again there keeps its path mappings and relay port: the way back its
  // warning describes.
  const { managementCandidate, sharingCandidate, peer } = node.sources;
  const wayBack = useWayBack(
    canManageFromHere(graph) ? server : undefined,
    [
      managementCandidate?.serverId === server?.serverId ? managementCandidate?.address : undefined,
      peer?.address,
      sharingCandidate?.address,
    ],
    context.ownHosts,
  );
  // Asked there already: the request is shown below, with its Cancel.
  const askedThere =
    !!wayBack.address &&
    liveOut.some((request) => sameMachine(request.address, wayBack.address, context.ownHosts));
  const failed = t("federation.management.failed");

  const revoke = (device: PairedDevice) =>
    confirm({
      title: t("federation.management.devices.revoke"),
      description: t("federation.management.devices.revokeConfirm", { name: device.name, target }),
      action: async () =>
        ensureOk(await BApi.remoteAccess.revokeRemoteAccessDevice(device.id, inline), failed),
      refresh: ["access"],
    });
  const approve = (request: ManagementRequest) =>
    confirm({
      title: t("federation.management.requests.approve"),
      description: t(
        request.remoteAddress
          ? "federation.management.requests.approveConfirmFrom"
          : "federation.management.requests.approveConfirm",
        { name: request.deviceName, address: request.remoteAddress, target },
      ),
      action: async () => {
        const approved = ensureOk(
          await BApi.remoteAccess.approveRemoteDevicePairingRequest(request.id, inline),
          failed,
        );

        actions.setNotice(
          t("federation.management.requests.approved", { name: request.deviceName, target }),
        );
        // The device it lets in joins the listing once it has collected its key: the details
        // wait for it there, and move to it.
        const deviceId = approved.data?.deviceId;

        if (deviceId) context.follow([identityKey.device(deviceId)], ["access"]);
      },
      refresh: ["access"],
    });

  return (
    <section
      aria-labelledby={`management-${node.id}`}
      className={sectionClass}
      data-testid="device-map-management"
    >
      <h3
        className={`flex items-center gap-2 text-sm font-semibold ${edgeStyles.management.text}`}
        id={`management-${node.id}`}
      >
        <KindBadge kind="management" />
        {t("federation.map.edge.management")}
      </h3>
      {!edge && (
        <p className="text-sm text-default-500">
          {t("federation.map.panel.management.none", { name })}
        </p>
      )}

      <DirectionRow
        direction="out"
        edge={edge}
        kind="management"
        name={name}
        testId="management-out"
      >
        {server && (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <span
                className={`rounded-md px-2 py-0.5 text-xs ${stateBadgeClass[server.state] ?? ""}`}
              >
                {t(`federation.servers.state.${server.state}`)}
              </span>
              {server.importedFromLegacyClient && (
                <span className="text-xs text-default-400">{t("federation.servers.imported")}</span>
              )}
            </div>
            <ManagedServerWarnings
              busy={busy}
              name={name}
              server={server}
              where="map"
              onOpen={(route) => void run(() => openManagedServer(server.serverId, route), [])}
            />
            {wayBack.checking && (
              <p className="text-xs text-default-500" data-testid="way-back-checking" role="status">
                {t("federation.map.panel.moved.checking", { name })}
              </p>
            )}
            {wayBack.address && !askedThere && (
              <ManageForm
                actions={actions}
                address={wayBack.address}
                expect={{ serverId: server.serverId, onGone: wayBack.recheck }}
                follow={context.follow}
                name={name}
                tip={t("federation.map.panel.moved.tip", { name, address: wayBack.address })}
                title={t("federation.map.panel.moved.title", { name, address: wayBack.address })}
              />
            )}
            <div className="flex flex-wrap items-center gap-2">
              <button
                className={primaryClass}
                disabled={busy}
                type="button"
                onClick={() => void run(() => openManagedServer(server.serverId), [])}
              >
                {t("federation.servers.open")}
              </button>
              <button
                className={`${buttonClass} text-danger`}
                disabled={busy}
                type="button"
                onClick={() =>
                  confirm({
                    title: t("federation.servers.forget"),
                    description: t("federation.servers.forgetConfirm", { name }),
                    action: () => managedServerApi.forget(server.serverId),
                    refresh: ["servers"],
                  })
                }
              >
                {t("federation.servers.forget")}
              </button>
            </div>
            <ManagedServerPathMappings
              busy={busy}
              className="pt-1"
              server={server}
              onSave={(mappings) =>
                run(async () => {
                  await managedServerApi.setPathMappings(server.serverId, mappings);
                  if (actions.mounted.current)
                    actions.setNotice(t("federation.servers.mappings.saved"));
                }, ["servers"])
              }
            />
          </>
        )}
        {
          // Beside a server too: asking again to manage one that moved joins its request to
          // it, by its id — and the request must be seen, and cancellable, where it is.
          liveOut.map((request) => {
            const minutes = minutesUntil(request.expiresAt);
            const retrying = request.outcome !== ManagedServerOutcome.AwaitingApproval;
            const label = request.serverName || request.address;

            return (
              <div
                key={request.requestId}
                className="flex flex-wrap items-center gap-2"
                data-testid="management-request-out"
              >
                <p
                  className={`mr-auto text-xs ${retrying ? "text-warning-600 dark:text-warning" : "text-default-500"}`}
                >
                  {t(retrying ? "federation.servers.retrying" : "federation.servers.waiting", {
                    name: label,
                    minutes,
                  })}
                  {server && (
                    <span className="block break-all text-default-400">{request.address}</span>
                  )}
                </p>
                <button
                  className={buttonClass}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    void run(() => managedServerApi.cancelRequest(request.requestId), ["servers"])
                  }
                >
                  {t("federation.servers.cancelRequest")}
                </button>
              </div>
            );
          })
        }
      </DirectionRow>
      {endedOut.map((request) => (
        <div
          key={request.requestId}
          className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-default-50 p-3 text-xs"
          data-testid="management-request-ended"
        >
          <p>
            {t(
              `federation.error.ManagedServer${ManagedServerOutcomeLabel[request.outcome] ?? request.outcome}`,
            )}
            {server && <span className="block break-all text-default-400">{request.address}</span>}
          </p>
          <button
            className={buttonClass}
            disabled={busy}
            type="button"
            onClick={() =>
              void run(() => managedServerApi.cancelRequest(request.requestId), ["servers"])
            }
          >
            {t("federation.servers.dismiss")}
          </button>
        </div>
      ))}

      <DirectionRow direction="in" edge={edge} kind="management" name={name} testId="management-in">
        {node.sources.managers.map((device) => (
          <div key={device.id} className="flex flex-wrap items-center gap-2">
            <p className="mr-auto text-xs text-default-500">
              {device.name} · {t(remoteDevicePlatformLabelKey(device.platform))} ·{" "}
              {device.lastSeenAt
                ? t("configuration.remoteAccess.devices.lastSeen", {
                    time: parseServerTime(device.lastSeenAt)?.toLocaleString() ?? "",
                  })
                : t("configuration.remoteAccess.devices.neverSeen")}
            </p>
            <button
              className={`${buttonClass} text-danger`}
              disabled={busy}
              type="button"
              onClick={() => revoke(device)}
            >
              {t("federation.management.devices.revoke")}
            </button>
          </div>
        ))}
        {node.sources.managementRequestsIn.map((request) => (
          <div key={request.id} className="space-y-2">
            <p className="text-xs text-default-500">
              {t(remoteDevicePlatformLabelKey(request.platform))}
              {request.remoteAddress ? ` · ${request.remoteAddress}` : ""} ·{" "}
              {t("configuration.remoteAccess.pending.expiresIn", {
                minutes: minutesUntil(request.expiresAt),
              })}
            </p>
            <div className="flex flex-wrap gap-2">
              <button
                className={primaryClass}
                disabled={busy}
                type="button"
                onClick={() => approve(request)}
              >
                {t("federation.management.requests.approve")}
              </button>
              <button
                className={buttonClass}
                disabled={busy}
                type="button"
                onClick={() =>
                  void run(async () => {
                    ensureOk(
                      await BApi.remoteAccess.rejectRemoteDevicePairingRequest(request.id, inline),
                      failed,
                    );
                  }, ["access"])
                }
              >
                {t("federation.management.requests.reject")}
              </button>
            </div>
          </div>
        ))}
        {node.sources.managersMatchedByName && (
          <p className="text-xs text-default-400">
            {t("federation.map.panel.matchedByName", { name: node.sources.managers[0]?.name })}
          </p>
        )}
      </DirectionRow>

      {canManage && (
        <ManageForm actions={actions} address={node.address!} follow={context.follow} name={name} />
      )}
    </section>
  );
}

/**
 * Where a managed server whose address answers as another install answers as itself now.
 * Each place it was last seen — found nearby by its own id, or where library sharing reaches
 * the same install — is asked afresh, and only an answer carrying the server's own id counts:
 * a state kept from an earlier conversation says nothing about who answers there today. A
 * place that is the server's own address under another spelling (`localhost` for `127.0.0.1`,
 * this device's LAN address for either) is never asked: someone else answers there.
 */
function useWayBack(
  server: ManagedServer | undefined,
  places: (string | null | undefined)[],
  ownHosts: ReadonlySet<string>,
) {
  const moved = server?.state === ManagedServerState.WrongServer ? server : undefined;
  const candidates: string[] = [];

  for (const place of places) {
    if (
      place &&
      moved &&
      !sameMachine(place, moved.address, ownHosts) &&
      !candidates.some((other) => sameMachine(other, place, ownHosts))
    )
      candidates.push(place);
  }
  const question = moved ? [moved.serverId, moved.address, ...candidates].join("\n") : "";
  const [answer, setAnswer] = useState<{ question: string; address?: string; done: boolean }>();
  const [round, setRound] = useState(0);

  useEffect(() => {
    if (!moved || !candidates.length) return;
    let current = true;

    setAnswer({ question, done: false });
    void (async () => {
      for (const place of candidates) {
        try {
          const probe = await managedServerApi.probe(withScheme(place));

          if (!current) return;
          if (probe.serverId === moved.serverId) {
            setAnswer({ question, address: place, done: true });

            return;
          }
        } catch {
          if (!current) return;
        }
      }
      if (current) setAnswer({ question, done: true });
    })();

    return () => {
      current = false;
    };
    // The question is everything asked: which server, and where.
  }, [question, round]);

  const settled = answer?.question === question ? answer : undefined;

  return {
    /** Confirmed just now to answer as the server. */
    address: settled?.address,
    checking: !!moved && candidates.length > 0 && !settled?.done,
    /** Ask again: the address found no longer answers as the server. */
    recheck: () => setRound((value) => value + 1),
  };
}

/** Pairs this device to manage another: with its code at once, or by a request it approves. */
function ManageForm({
  actions,
  address,
  name,
  follow,
  title,
  tip,
  expect,
}: {
  actions: PanelActions;
  address: string;
  name: string;
  follow: PanelContext["follow"];
  /** In place of the usual heading and explanation, e.g. to pair again at a new address. */
  title?: string;
  tip?: string;
  /**
   * The server that must answer there: asked again right before pairing, since an address
   * can change hands between being found and being used. When another answers, nothing is
   * sent and `onGone` is told.
   */
  expect?: { serverId: string; onGone: () => void };
}) {
  const { t } = useTranslation();
  const [code, setCode] = useState("");

  const describe = (result: ManagedServerPairing) => {
    const label = result.serverName || name;

    if (result.outcome === ManagedServerOutcome.Ok)
      return t("federation.servers.paired", { name: label });
    if (result.outcome === ManagedServerOutcome.AwaitingApproval)
      return t("federation.servers.requested", { name: label });
    throw outcomeError(result.outcome, result.detail);
  };

  return (
    <form
      aria-label={title ?? t("federation.map.panel.manage", { name })}
      className="space-y-2 rounded-lg bg-default-50 p-3"
      data-testid="manage-form"
      onSubmit={(event) => {
        event.preventDefault();
        void actions.run(async () => {
          if (expect) {
            const answer = await managedServerApi.probe(withScheme(address));

            if (answer.serverId !== expect.serverId) {
              expect.onGone();
              throw new MessageError(t("federation.map.panel.moved.gone", { name, address }));
            }
          }
          const result = await managedServerApi.pair(withScheme(address), code.trim() || undefined);

          actions.setNotice(describe(result));
          // A device found nearby becomes a request, or a managed server.
          follow([
            result.requestId && identityKey.request(result.requestId),
            result.serverId && identityKey.install(result.serverId),
            identityKey.address(address),
          ]);
          if (actions.mounted.current) setCode("");
        }, ["servers"]);
      }}
    >
      <p className="text-sm font-medium">{title ?? t("federation.map.panel.manage", { name })}</p>
      <p className="text-xs text-default-500">
        {tip ?? t("federation.map.panel.manageTip", { name })}
      </p>
      <label className="block space-y-1 text-xs">
        <span>{t("federation.servers.add.code")}</span>
        <input
          autoComplete="off"
          className={fieldClass}
          value={code}
          onChange={(event) => setCode(event.target.value)}
        />
      </label>
      <button className={primaryClass} disabled={actions.busy} type="submit">
        {t(code.trim() ? "federation.servers.add.withCode" : "federation.servers.add.request")}
      </button>
    </form>
  );
}

/** This device: its name, what it has turned on, and where its settings are. */
function SelfDetails({ context }: { context: PanelContext }) {
  const { t } = useTranslation();
  const { graph, status, access, actions } = context;
  const [editing, setEditing] = useState<string>();
  const mode = access?.mode;
  const management =
    mode === undefined
      ? undefined
      : mode === RemoteAccessMode.Disabled
        ? "off"
        : mode === RemoteAccessMode.Unrestricted
          ? "unrestricted"
          : access?.requirePairing
            ? "paired"
            : "open";
  const count = (kind: MapEdgeKind, direction: "in" | "out") =>
    graph.edges.filter((edge) => edge.kind === kind && edge[direction] === "active").length;
  const counts = [
    ["sharesWith", count("sharing", "out")],
    ["browses", count("sharing", "in")],
    ["manages", count("management", "out")],
    ["managedBy", count("management", "in")],
  ] as const;

  return (
    <div className="space-y-4">
      {status && (
        <section className={sectionClass}>
          {editing === undefined ? (
            <button
              className={buttonClass}
              disabled={actions.busy}
              type="button"
              onClick={() => setEditing(status.identity.name)}
            >
              {t("federation.name.edit")}
            </button>
          ) : (
            <form
              className="space-y-2"
              onSubmit={(event) => {
                event.preventDefault();
                const name = editing.trim();

                void actions.run(async () => {
                  await federationPeerApi.setName(name || null);
                  if (actions.mounted.current) setEditing(undefined);
                }, ["sharing"]);
              }}
            >
              <label className="block space-y-1 text-sm">
                <span>{t("federation.name.label")}</span>
                <input
                  // eslint-disable-next-line jsx-a11y/no-autofocus
                  autoFocus
                  className={fieldClass}
                  maxLength={64}
                  value={editing}
                  onChange={(event) => setEditing(event.target.value)}
                />
              </label>
              <p className="text-xs text-default-500">{t("federation.name.tip")}</p>
              <div className="flex flex-wrap gap-2">
                <button className={primaryClass} disabled={actions.busy} type="submit">
                  {t("federation.save")}
                </button>
                <button
                  className={buttonClass}
                  disabled={actions.busy}
                  type="button"
                  onClick={() =>
                    void actions.run(async () => {
                      await federationPeerApi.setName(null);
                      if (actions.mounted.current) setEditing(undefined);
                    }, ["sharing"])
                  }
                >
                  {t("federation.name.reset")}
                </button>
                <button
                  className={buttonClass}
                  disabled={actions.busy}
                  type="button"
                  onClick={() => setEditing(undefined)}
                >
                  {t("federation.cancel")}
                </button>
              </div>
            </form>
          )}
        </section>
      )}
      <section className={sectionClass}>
        <ul className="space-y-1.5 text-sm" data-testid="device-map-self-status">
          {status && (
            <li className="flex justify-between gap-3">
              <span className="text-default-500">{t("federation.map.edge.sharing")}</span>
              <span>
                {t(status.sharingEnabled ? "federation.sharing.on" : "federation.sharing.off")}
              </span>
            </li>
          )}
          {status && (
            <li className="flex justify-between gap-3">
              <span className="text-default-500">{t("federation.browsing.title")}</span>
              <span>
                {t(status.browsingEnabled ? "federation.browsing.on" : "federation.browsing.off")}
              </span>
            </li>
          )}
          {management && (
            <li className="flex justify-between gap-3">
              <span className="text-default-500">
                {t("federation.map.panel.self.managedByOthers")}
              </span>
              <span>{t(`federation.management.status.${management}`)}</span>
            </li>
          )}
        </ul>
        <dl className="grid grid-cols-2 gap-2">
          {counts.map(([key, value]) => (
            <div key={key} className="rounded-lg bg-default-50 p-2">
              <dt className="text-xs text-default-500">{t(`federation.map.panel.self.${key}`)}</dt>
              <dd className="text-lg font-semibold">{value}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section className="flex flex-wrap gap-2 border-t border-default-200 pt-4">
        <Link className={buttonClass} to={devicesRoute()}>
          {t("federation.devices.title")}
        </Link>
        <Link className={buttonClass} to={devicesRoute("management")}>
          {t("federation.map.panel.self.managementSettings")}
        </Link>
      </section>
    </div>
  );
}

/** A device found nearby: the ways to connect to it that it answered to. */
function GhostDetails({ context, node }: { context: PanelContext; node: MapNode }) {
  const { t } = useTranslation();
  const { graph, status, actions } = context;
  const name = nodeName(t, node);
  const { sharingCandidate, managementCandidate } = node.sources;
  const manageable = !!managementCandidate && canManageFromHere(graph);

  return (
    <div className="space-y-3">
      <ul className="space-y-1 text-xs text-default-500">
        {sharingCandidate && <li>{t("federation.map.panel.ghost.sharing")}</li>}
        {managementCandidate && <li>{t("federation.map.panel.ghost.management")}</li>}
      </ul>
      {sharingCandidate && (
        <SharingRequestForm
          actions={actions}
          address={sharingCandidate.address}
          follow={context.follow}
          remoteDisabled={status?.remoteAccessMode === RemoteAccessMode.Disabled}
        />
      )}
      {manageable && (
        <ManageForm
          actions={actions}
          address={managementCandidate!.address}
          follow={context.follow}
          name={name}
        />
      )}
      {!sharingCandidate && !manageable && (
        <p className="text-sm text-default-500">{t("federation.map.panel.ghost.nothing")}</p>
      )}
    </div>
  );
}
