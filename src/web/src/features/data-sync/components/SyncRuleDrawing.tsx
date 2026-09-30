import type { TFunction } from "i18next";
import type { ReactNode } from "react";
import type { DataSyncLinkResult } from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { LaneStatus, ModeName, StatusLine, SyncPeer } from "../viewModels";
import type { RemoteAccessMode } from "@/sdk/constants";

import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router-dom";

import { dataSyncApi } from "../api";
import {
  dataSyncInboxRoute,
  dataSyncLinkRoute,
  dataSyncRestoreRoute,
  dataSyncReviewRoute,
} from "../routes";
import { useOpenPeerDataSync } from "../hooks/useOpenPeerDataSync";
import {
  canStartAnyway,
  dataSyncKinds,
  hasPeerError,
  linkEditor,
  linkNotes,
  linkStatus,
  modeValue,
  offersAskToKeepInStep,
  orderKinds,
  pausedAsRestored,
  receivePhrase,
  sharingNeeded,
  toggleKind,
  twoWayConfirmation,
} from "../viewModels";

import { linkButtonClass, smallButtonClass, StatusDot, syncText, toneText } from "./common";
import { DecideThereHint } from "./ElsewhereLines";
import { openCodeFirst, turnOnSharing } from "./SharingControls";

import { edgeStyles, KindBadgeGlyph } from "@/features/federation/map/DeviceMapCanvas";
import {
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncResumeAction,
} from "@/sdk/constants";

/*
 * The rule editor: the other device above this one, the arrow along which the other may read
 * this device's definitions and the arrow along which this device receives the other's — drawn,
 * never pressed — and one control for each choice (spec §11.1): the mode buttons for receiving,
 * the kind chips for what it receives, and [Stop X reading] (or the ways to let it read) for the
 * other direction.
 */

export interface SyncRuleDrawingProps {
  peer: SyncPeer;
  actions: DataSyncPanelActions;
  /**
   * Whether this window may create or widen access (spec §7.1.5). Where it may not, the
   * controls that would are unavailable, with a note saying where to do it.
   */
  canManage: boolean;
  /** This device's own switch and remote access: what keeping in step both ways needs. */
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode;
  /** This device's name as the other device will be told it, in a request's preview. */
  selfName: string;
  /** Opens the one-time code for the other device to redeem. */
  onCreateCode?: () => void;
  /** Where the page is not the host (the device map): links to the page's own details. */
  linkToPage?: boolean;
  /** A new link or copy was made (the wizard closes), before a copy's first sync is opened. */
  onCreated?: (result: DataSyncLinkResult) => void;
  now?: number;
}

export default function SyncRuleDrawing({
  peer,
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
  selfName,
  onCreateCode,
  linkToPage = false,
  onCreated,
  now,
}: SyncRuleDrawingProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const editor = linkEditor(peer);
  // The kinds a new link will receive, chosen before it exists.
  const [draftKinds, setDraftKinds] = useState<string[]>(editor.kinds);
  const kinds = peer.linkId === undefined ? draftKinds : editor.kinds;
  const name = peer.name;
  const status = linkStatus(t, peer, now);
  const notes = linkNotes(t, peer, now);
  const modeLegendId = useId();

  const own = { sharingEnabled, remoteAccessMode };
  /** Sharing (and remote access) this device must turn on before the other can read it. */
  const mustTurnOn = sharingNeeded(own);
  /**
   * Who a new link or copy goes to: an address and a code, or the device by its id and — where it
   * is no device the server knows yet (one found nearby) — the address its request goes to.
   */
  const destination = peer.code
    ? { address: peer.address, code: peer.code }
    : {
        peerNodeId: peer.nodeId,
        ...(peer.linkId === undefined && peer.address ? { address: peer.address } : {}),
      };
  /** Whether switching to `target` would create or widen access (§7.1.5). */
  const createsAccess = (target: ModeName) =>
    target !== "off" &&
    ((peer.linkId === undefined && peer.weMayRead !== true) ||
      (target === "twoWay" && !peer.peerMayRead));
  const allowed = (target: ModeName) => canManage || !createsAccess(target);

  const setMode = (target: ModeName) => {
    if (target === editor.mode || !allowed(target)) return;
    if (target === "off") {
      if (peer.linkId === undefined) return;
      const linkId = peer.linkId;

      actions.confirm({
        title: t("dataSync.off.title", { name }),
        description: t("dataSync.off.description", { name }),
        warning: peer.peerMayRead ? t("dataSync.off.stillReads", { name }) : undefined,
        action: () => dataSyncApi.updateLink(linkId, { mode: DataSyncLinkMode.Off }),
        refresh: ["dataSync"],
      });

      return;
    }
    // A code is redeemed one way (§7.2.3): the link then offers to ask the other device to read this one.
    const twoWay = target === "twoWay" && !peer.code;
    const turnsOn = twoWay && !peer.peerMayRead && mustTurnOn;
    const operation = async () => {
      if (turnsOn) await turnOnSharing(own);
      try {
        if (peer.linkId !== undefined)
          await dataSyncApi.updateLink(peer.linkId, { mode: modeValue(target) });
        else {
          const result = await dataSyncApi.createLink({
            ...destination,
            mode: modeValue(target),
            kinds,
          });

          onCreated?.(result);
        }
      } catch (cause) {
        // Nothing was made: sharing is turned off again when this turned it on. Remote access,
        // which this may have turned on too, is a setting of its own and stays as it is now.
        if (turnsOn && !sharingEnabled)
          await dataSyncApi
            .setSharing({ enabled: false, enablePairedRemoteAccess: false })
            .catch(() => undefined);
        throw cause;
      }
    };

    if (!createsAccess(target)) {
      void actions.run(operation, ["dataSync"]);

      return;
    }

    actions.confirm(
      twoWay
        ? {
            ...twoWayConfirmation(t, name, own, turnsOn),
            action: operation,
            refresh: ["dataSync"],
          }
        : {
            title: t("dataSync.follow.title", { name }),
            description: t("dataSync.follow.confirm", { name }),
            warning: t("dataSync.request.follow", { name: selfName }),
            action: operation,
            refresh: ["dataSync"],
          },
    );
  };

  const setKinds = (kind: string) => {
    const next = toggleKind(kinds, kind);

    if (!next) return;
    if (peer.linkId === undefined) {
      setDraftKinds(next);

      return;
    }
    const linkId = peer.linkId;

    void actions.run(() => dataSyncApi.updateLink(linkId, { kinds: next }), ["dataSync"]);
  };

  const stopReading = () =>
    actions.confirm({
      title: t("dataSync.arrow.read.stopTitle", { name }),
      description: t("dataSync.arrow.read.stopDescription", { name }),
      action: () => dataSyncApi.revokeReader(peer.nodeId),
      refresh: ["dataSync"],
    });

  const copyOnce = () =>
    actions.confirm({
      title: t("dataSync.copyOnce.title", { name }),
      description: t("dataSync.copyOnce.description", { name }),
      warning:
        peer.weMayRead === true ? undefined : t("dataSync.request.follow", { name: selfName }),
      action: async () => {
        const result = await dataSyncApi.copyOnce({ ...destination, kinds });
        const link = result.link;

        if (!link || !actions.mounted.current) return;
        onCreated?.(result);
        // A copy this device may not read yet sent a request: its first sync comes once it is
        // approved there, which may take hours. Said here, where the request now shows.
        if (link.state === DataSyncLinkState.AwaitingAccess)
          actions.setNotice(t("dataSync.wizard.requested", { name }));
        else navigate(dataSyncReviewRoute(link.id));
      },
      refresh: ["dataSync"],
    });

  const readLabel =
    editor.read === "active"
      ? t("federation.map.direction.sync.out.active", { name })
      : t("dataSync.arrow.read.off", { name });
  const copyOnceOffered =
    editor.mode === "off" &&
    (peer.state === undefined || peer.state === DataSyncLinkState.Stopped) &&
    peer.outcome !== "awaitingApproval";
  const manageNote = !canManage && (["follow", "twoWay"] as const).some((mode) => !allowed(mode));

  return (
    <div className="space-y-3" data-testid="data-sync-rule-drawing">
      <div className="flex flex-col items-stretch gap-2">
        <DeviceBox subtitle={t("dataSync.otherDevice")} title={name} />
        <DirectionArrow
          direction="up"
          label={readLabel}
          status={editor.read}
          testId="data-sync-arrow-read"
          text={t("dataSync.arrow.read.label")}
        />
        <PeerKinds kinds={editor.read === "active" ? peer.peerKinds : undefined} />
        <DirectionArrow
          direction="down"
          label={receivePhrase(t, peer, name)}
          status={editor.receive}
          testId="data-sync-arrow-receive"
          text={t("dataSync.arrow.receive.label")}
        />
        <div className="flex flex-wrap items-center justify-center gap-1.5 px-1">
          {orderedChips(kinds).map((kind) => {
            const on = kinds.includes(kind);
            const locked = on && kinds.length === 1;

            return (
              <button
                key={kind}
                aria-disabled={locked ? "true" : undefined}
                aria-pressed={on}
                className={`rounded-full border px-2 py-0.5 text-[11px] transition ${
                  on
                    ? `border-secondary bg-secondary/10 ${syncText}`
                    : "border-default-300 text-default-500 hover:bg-default-100"
                } ${locked ? "cursor-not-allowed" : ""}`}
                data-kind={kind}
                data-testid="data-sync-kind-chip"
                disabled={actions.busy}
                title={locked ? t("dataSync.kind.lastOne") : undefined}
                type="button"
                onClick={() => !locked && setKinds(kind)}
              >
                {t(`dataSync.kind.${kind}`, { defaultValue: kind })}
              </button>
            );
          })}
        </div>
        <DeviceBox self subtitle={t("dataSync.thisDevice")} title={selfName} />
      </div>

      {editor.read === "active" ? (
        <button
          className={smallButtonClass}
          data-testid="data-sync-stop-reading"
          disabled={actions.busy}
          type="button"
          onClick={stopReading}
        >
          {t("dataSync.arrow.read.stop", { name })}
        </button>
      ) : (
        canManage && (
          <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
            <span>{t("dataSync.arrow.read.offHint", { name })}</span>
            {onCreateCode && (
              <button
                className={smallButtonClass}
                data-testid="data-sync-create-code-for"
                disabled={actions.busy}
                type="button"
                onClick={() => openCodeFirst(t, actions, own, onCreateCode, name)}
              >
                {t("dataSync.invitation.createFor", { name })}
              </button>
            )}
          </div>
        )
      )}

      <div className="space-y-1.5">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5">
          {/*
           * The modes as buttons pressed one at a time, never radios: a radio group selects as
           * the arrow keys move through it, and each of these changes the link at once. Only
           * Enter, Space or a click changes it; the arrow keys move nothing here.
           */}
          <div
            aria-labelledby={modeLegendId}
            className="inline-flex flex-wrap rounded-lg border border-default-300 p-0.5"
            data-testid="data-sync-modes"
            role="group"
          >
            <span className="sr-only" id={modeLegendId}>
              {t("dataSync.mode.legend", { name })}
            </span>
            {(["off", "follow", "twoWay"] as const).map((mode) => (
              <button
                key={mode}
                aria-pressed={editor.mode === mode}
                className={`rounded-md px-2.5 py-1 text-sm outline-none transition focus-visible:ring-2 focus-visible:ring-focus disabled:cursor-not-allowed ${
                  editor.mode === mode
                    ? `bg-secondary/15 font-medium ${syncText}`
                    : "hover:bg-default-100 disabled:opacity-60"
                }`}
                data-mode={mode}
                data-testid={`data-sync-mode-${mode}`}
                disabled={actions.busy || !allowed(mode)}
                type="button"
                onClick={() => setMode(mode)}
              >
                {t(`dataSync.mode.${mode}`)}
              </button>
            ))}
          </div>
          {copyOnceOffered && (
            <button
              className={`${smallButtonClass} ml-auto`}
              data-testid="data-sync-copy-once"
              disabled={actions.busy || (!canManage && peer.weMayRead !== true)}
              type="button"
              onClick={copyOnce}
            >
              {t("dataSync.copyOnce.button")}
            </button>
          )}
        </div>
        {manageNote && (
          <p className="text-xs text-default-500" data-testid="data-sync-manage-elsewhere">
            {t("dataSync.manageElsewhere")}
          </p>
        )}
      </div>

      <StatusBlock
        actions={actions}
        canManage={canManage}
        linkToPage={linkToPage}
        notes={notes}
        now={now}
        peer={peer}
        status={status}
        onStop={() => setMode("off")}
      />
    </div>
  );
}

/** A device as the drawing shows it: a rounded card with its name. */
function DeviceBox({
  title,
  subtitle,
  self = false,
}: {
  title: string;
  subtitle: string;
  self?: boolean;
}) {
  return (
    <div
      className={`min-w-0 rounded-xl border px-2.5 py-2 text-center ${
        self ? "border-primary bg-primary-50" : "border-default-300 bg-content1"
      }`}
    >
      <p className="truncate text-sm font-semibold" title={title}>
        {title}
      </p>
      <p className={`truncate text-[11px] ${self ? "text-primary-700" : "text-default-500"}`}>
        {subtitle}
      </p>
    </div>
  );
}

/**
 * One direction, drawn and named: solid when working, dashed while it waits, a faint dotted
 * line when off. In the device map's colour for data sync, pointing to the device that receives
 * — up to the other device for reading, down to this one for receiving.
 */
function DirectionArrow({
  direction,
  status,
  label,
  text,
  testId,
}: {
  direction: "up" | "down";
  status: LaneStatus;
  label: string;
  text: string;
  testId: string;
}) {
  const lineStyle =
    status === "active"
      ? "border-solid"
      : status === "pending"
        ? "border-dashed"
        : "border-dotted opacity-50";
  const caption = <span className={`text-[11px] font-medium ${syncText}`}>{text}</span>;
  const head = (
    <svg
      aria-hidden
      className={`shrink-0 ${status === "none" ? "fill-default-400 opacity-50" : edgeStyles.sync.fill}`}
      height={10}
      viewBox="0 0 10 10"
      width={10}
    >
      <path
        d={direction === "up" ? "M0.5 10 L5 0.5 L9.5 10 L5 7.5 z" : "M0.5 0 L5 9.5 L9.5 0 L5 2.5 z"}
      />
    </svg>
  );

  return (
    <div
      aria-label={label}
      className="flex flex-col items-center px-1 py-1.5"
      data-status={status}
      data-testid={testId}
      role="img"
      title={label}
    >
      {direction === "up" && caption}
      {direction === "up" && head}
      <span
        className={`relative h-8 border-l-2 ${lineStyle} ${
          status === "none" ? "border-default-400" : "border-secondary"
        }`}
      >
        {status === "active" && (
          <svg
            aria-hidden
            className="absolute left-1/2 top-1/2 h-4 w-4 -translate-x-1/2 -translate-y-1/2"
            viewBox="-12 -12 24 24"
          >
            <circle
              className={`fill-content1 ${edgeStyles.sync.stroke}`}
              r={10}
              strokeWidth={1.5}
            />
            <KindBadgeGlyph className={edgeStyles.sync.stroke} kind="sync" />
          </svg>
        )}
      </span>
      {direction === "down" && head}
      {direction === "down" && caption}
    </div>
  );
}

/** Every kind a chip can be shown for: this build's, and any other the link names. */
const orderedChips = (kinds: string[]) => orderKinds([...new Set([...dataSyncKinds, ...kinds])]);

/** The kinds the other device receives from this one: said, not changed from here. */
function PeerKinds({ kinds }: { kinds?: string[] }) {
  const { t } = useTranslation();

  if (!kinds?.length) return null;

  return (
    <div
      className="flex flex-wrap items-center justify-center gap-1.5 px-1"
      data-testid="data-sync-peer-kinds"
    >
      {kinds.map((kind) => (
        <span
          key={kind}
          className={`rounded-full border border-dashed border-secondary/60 px-2 py-0.5 text-[11px] ${syncText}`}
        >
          {t(`dataSync.kind.${kind}`, { defaultValue: kind })}
        </span>
      ))}
    </div>
  );
}

/** The status line of §11.6, with what can be done about it, and what the details add. */
function StatusBlock({
  peer,
  status,
  notes,
  actions,
  canManage,
  linkToPage,
  now,
  onStop,
}: {
  peer: SyncPeer;
  status: StatusLine;
  notes: StatusLine[];
  actions: DataSyncPanelActions;
  canManage: boolean;
  linkToPage: boolean;
  now?: number;
  onStop: () => void;
}) {
  const { t } = useTranslation();
  const name = peer.name;
  const linkId = peer.linkId;
  // Decisions that wait on the other device are taken there: this window switches to it where
  // it manages that device.
  const elsewhere = useOpenPeerDataSync((peer.attention?.openDecisions ?? 0) > 0);
  const button = (label: string, onClick: () => void, testId?: string) => (
    <button
      key={label}
      className={smallButtonClass}
      data-testid={testId}
      disabled={actions.busy}
      type="button"
      onClick={onClick}
    >
      {label}
    </button>
  );
  const resume = (action: DataSyncResumeAction, label: string, testId?: string) =>
    linkId === undefined
      ? null
      : button(
          label,
          () => void actions.run(() => dataSyncApi.resumeLink(linkId, action), ["dataSync"]),
          testId,
        );
  const syncNow =
    linkId === undefined
      ? null
      : button(
          t("dataSync.link.syncNow"),
          () => void actions.run(() => dataSyncApi.syncNow(linkId), ["dataSync"]),
          "data-sync-sync-now",
        );
  const askAgain = (label: string) =>
    canManage ? resume(DataSyncResumeAction.AskAccessAgain, label) : null;
  const stop = button(t("dataSync.link.stop"), onStop);
  const inbox = (
    <Link key="inbox" className={smallButtonClass} to={dataSyncInboxRoute(peer.nodeId)}>
      {t("dataSync.link.openNeedsYou")}
    </Link>
  );

  const statusActions: ReactNode[] = [];

  switch (status.code) {
    case "AccessRejected":
    case "AccessExpired":
      if (linkId !== undefined)
        statusActions.push(
          button(
            t("dataSync.link.dismiss"),
            () => void actions.run(() => dataSyncApi.resetLink(linkId), ["dataSync"]),
            "data-sync-dismiss",
          ),
        );
      break;
    case "AwaitingReview":
      if (linkId !== undefined)
        statusActions.push(
          <Link key="review" className={smallButtonClass} to={dataSyncReviewRoute(linkId)}>
            {t("dataSync.link.review")}
          </Link>,
        );
      break;
    case "Paused.ByUser":
      statusActions.push(resume(DataSyncResumeAction.Resume, t("dataSync.pause.resume")));
      break;
    case "Paused.AllPaused":
      statusActions.push(
        button(
          t("dataSync.pause.resumeAll"),
          () => void actions.run(() => dataSyncApi.setAllPaused(false), ["dataSync"]),
        ),
      );
      break;
    case "Paused.PeerReset":
      statusActions.push(askAgain(t("dataSync.pause.askAgain", { name })), stop);
      break;
    case "Paused.PeerResetRestored":
      statusActions.push(resume(DataSyncResumeAction.Resume, t("dataSync.pause.resume")), stop);
      break;
    case "Paused.PeerIdentityDuplicated":
      statusActions.push(resume(DataSyncResumeAction.Resume, t("dataSync.pause.resume")));
      break;
    case "Paused.LocalRestoreDetected":
    case "Paused.LocalRestoreSuspected":
      statusActions.push(
        <Link key="restore" className={smallButtonClass} to={dataSyncRestoreRoute}>
          {t("dataSync.pause.openRestore")}
        </Link>,
      );
      break;
    case "AccessRevoked":
      // The same line says the other device turned sharing off, where asking again cannot help:
      // it answers the moment sharing is back on.
      if (peer.lastErrorCode === "AccessRevoked")
        statusActions.push(askAgain(t("dataSync.pause.askAgain", { name })));
      break;
    case "ReadBackFailed":
      // Asks the other device again, as a request of this device's own (spec §7.2.4).
      if (canManage && linkId !== undefined)
        statusActions.push(
          button(
            t("dataSync.link.tryAgain"),
            () =>
              void actions.run(
                () => dataSyncApi.resumeLink(linkId, DataSyncResumeAction.AskAccessAgain),
                ["dataSync"],
              ),
            "data-sync-read-back-again",
          ),
        );
      break;
    case "NeedsYou":
      statusActions.push(inbox);
      break;
    case "WaitingForPeerReview":
      // After a week the other device's review is not waited for any more (spec §8.3).
      if (canStartAnyway(peer, now))
        statusActions.push(
          resume(
            DataSyncResumeAction.StartAnyway,
            t("dataSync.pause.startAnyway"),
            "data-sync-start-anyway",
          ),
        );
      break;
    case "Failed":
      statusActions.push(
        linkId === undefined
          ? null
          : button(
              t("dataSync.retry"),
              () => void actions.run(() => dataSyncApi.syncNow(linkId), ["dataSync"]),
            ),
      );
      break;
    default:
      break;
  }
  const running =
    peer.state === DataSyncLinkState.Active &&
    linkId !== undefined &&
    status.code !== "Failed" &&
    !hasPeerError(peer);
  const detail =
    peer.state === DataSyncLinkState.Paused
      ? pauseHint(t, peer)
      : status.code === "AwaitingAccess"
        ? t("dataSync.link.approveThere", { name })
        : status.code === "ReadBackFailed"
          ? canManage
            ? t("dataSync.link.readBackAgain", { name })
            : t("dataSync.manageElsewhere")
          : canStartAnyway(peer, now)
            ? t("dataSync.pause.startAnywayHint", { name })
            : undefined;

  return (
    <div className="space-y-2 rounded-lg bg-default-50 p-2.5" data-testid="data-sync-status">
      <div className="flex items-start gap-2 text-sm">
        <StatusDot tone={status.tone} />
        <p className={`min-w-0 flex-1 ${toneText[status.tone]}`} data-status-code={status.code}>
          {status.text}
        </p>
      </div>
      {detail && <p className="text-xs text-default-500">{detail}</p>}
      {notes.map((note) => (
        <div
          key={note.code}
          className="flex flex-wrap items-center gap-2 text-xs"
          data-note={note.code}
        >
          <span className={toneText[note.tone]}>{note.text}</span>
          {note.code === "ReadBackDeclined" &&
            offersAskToKeepInStep(peer) &&
            askAgain(t("dataSync.link.askKeepInStep", { name }))}
          {note.code === "NeedsYouThere" &&
            (elsewhere.canOpen(peer.nodeId) ? (
              button(
                t("dataSync.inbox.elsewhere.open", { name }),
                () => void actions.run(() => elsewhere.open(peer.nodeId), []),
                "data-sync-open-there",
              )
            ) : (
              <DecideThereHint name={name} pairable={elsewhere.canPair} />
            ))}
        </div>
      ))}
      {(statusActions.some(Boolean) || running || linkToPage) && (
        <div className="flex flex-wrap items-center gap-2">
          {statusActions}
          {running && syncNow}
          {running &&
            linkId !== undefined &&
            button(
              t("dataSync.link.pause"),
              () => void actions.run(() => dataSyncApi.pauseLink(linkId), ["dataSync"]),
            )}
          {linkToPage && linkId !== undefined && (
            <Link className={linkButtonClass} to={dataSyncLinkRoute(linkId)}>
              {t("dataSync.link.openPage")}
            </Link>
          )}
        </div>
      )}
      {/* What is not synced with it, where only the page lists it (spec §11.1). */}
      {linkToPage && linkId !== undefined && (peer.excludedCount > 0 || peer.heldCount > 0) && (
        <div className="space-y-0.5 text-xs text-default-500" data-testid="data-sync-not-synced">
          {peer.excludedCount > 0 && (
            <p className="flex flex-wrap items-center gap-2" data-count="skipped">
              <span>{t("dataSync.link.skipped", { count: peer.excludedCount })}</span>
              <Link className={linkButtonClass} to={dataSyncLinkRoute(linkId)}>
                {t("dataSync.link.show")}
              </Link>
            </p>
          )}
          {peer.heldCount > 0 && (
            <p data-count="withheld">{t("dataSync.link.withheld", { count: peer.heldCount })}</p>
          )}
        </div>
      )}
    </div>
  );
}

/** What a pause asks of the reader, beyond the line that names it. */
const pauseHint = (t: TFunction, peer: SyncPeer) => {
  switch (peer.pausedReason) {
    case DataSyncPauseReason.PeerReset:
      return pausedAsRestored(peer)
        ? t("dataSync.pause.restoredHint", { name: peer.name })
        : t("dataSync.pause.resetHint", { name: peer.name });
    case DataSyncPauseReason.PeerIdentityDuplicated:
      // The way to the reset, in the words the Devices page uses for it, so they cannot drift.
      return t("dataSync.pause.duplicatedHint", {
        name: peer.name,
        mode: t("federation.mode"),
        page: t("federation.devices.title"),
        section: t("federation.devices.tab.advanced"),
        panel: t("federation.identity.title"),
        action: t("federation.identity.reset"),
      });
    default:
      return undefined;
  }
};
