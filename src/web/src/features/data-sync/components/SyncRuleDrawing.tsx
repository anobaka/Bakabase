import type { TFunction } from "i18next";
import type { KeyboardEvent, ReactNode } from "react";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { LinkEditor, ModeName, StatusLine, SyncPeer } from "../viewModels";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router-dom";

import { dataSyncApi } from "../api";
import {
  dataSyncInboxRoute,
  dataSyncLinkRoute,
  dataSyncRestoreRoute,
  dataSyncReviewRoute,
} from "../routes";
import { useElementWidth } from "../hooks/useElementWidth";
import { useMenuKeyboard } from "../hooks/useMenuKeyboard";
import { useOpenPeerDataSync } from "../hooks/useOpenPeerDataSync";
import {
  canStartAnyway,
  dataSyncKinds,
  linkEditor,
  linkNotes,
  linkStatus,
  modeValue,
  offersAskToKeepInStep,
  orderKinds,
  pauseDetail,
  receiveToggleTarget,
  sharingNeeded,
  stillReadsWhileOff,
  toggleKind,
  turnsOnWarning,
  twoWayConfirmation,
} from "../viewModels";

import { linkButtonClass, smallButtonClass, StatusDot, syncText, toneText } from "./common";

import { edgeStyles, KindBadgeGlyph } from "@/features/federation/map/DeviceMapCanvas";
import {
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncResumeAction,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * The rule editor, drawn: this device and the other one, the arrow along which this device
 * receives the other's definitions, and the arrow along which the other may read this
 * device's. The arrows are the controls; the mode buttons under them are the same choice in
 * words, and always say the same as the arrows (spec §11.1).
 */

/** Below this width the devices stand one above the other, the arrows between them vertical. */
export const HORIZONTAL_MIN_WIDTH = 420;

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
  /** The width to lay out for before the drawing can be measured (tests, first paint). */
  initialWidth?: number;
  now?: number;
}

const ARROW_TEXT_SIZE = "text-[11px]";

const activate = (run: () => void) => (event: KeyboardEvent<HTMLElement>) => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    run();
  }
};

/** A space's own activation, which a native button runs on key up: it was taken on key down. */
const swallowSpaceUp = (event: KeyboardEvent<HTMLElement>) => {
  if (event.key === " ") event.preventDefault();
};

export default function SyncRuleDrawing({
  peer,
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
  selfName,
  onCreateCode,
  linkToPage = false,
  initialWidth = 520,
  now,
}: SyncRuleDrawingProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { ref, width } = useElementWidth<HTMLDivElement>(initialWidth);
  const horizontal = width >= HORIZONTAL_MIN_WIDTH;
  const editor = linkEditor(peer);
  // The kinds a new link will receive, chosen before it exists.
  const [draftKinds, setDraftKinds] = useState<string[]>(editor.kinds);
  const kinds = peer.linkId === undefined ? draftKinds : editor.kinds;
  const name = peer.name;
  const status = linkStatus(t, peer, now);
  const notes = linkNotes(t, peer, now);
  const radioName = useId();

  const own = { sharingEnabled, remoteAccessMode };
  /** Sharing (and remote access) this device must turn on before the other can read it. */
  const mustTurnOn = sharingNeeded(own);
  /** Turns on what the other device needs to read this one: sharing, and remote access if off. */
  const turnOnSharing = () =>
    dataSyncApi.setSharing({
      enabled: true,
      enablePairedRemoteAccess: remoteAccessMode === RemoteAccessMode.Disabled,
    });
  /**
   * Who a new link or copy goes to: the device by its id, and — where it is no device the server
   * knows yet (one found nearby on the device map) — the address its request goes to.
   */
  const destination = {
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
    const twoWay = target === "twoWay";
    const turnsOn = twoWay && !peer.peerMayRead && mustTurnOn;
    const operation = async () => {
      if (turnsOn) await turnOnSharing();
      if (peer.linkId !== undefined)
        await dataSyncApi.updateLink(peer.linkId, { mode: modeValue(target) });
      else await dataSyncApi.createLink({ ...destination, mode: modeValue(target), kinds });
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
        const linkId = result.linkId ?? undefined;

        if (linkId === undefined) return;
        // A copy this device may not read yet sent a request: its review comes once it is
        // approved there, which may take hours. Said here, where the request now shows.
        const requested =
          !result.reviewId &&
          peer.weMayRead !== true &&
          (await dataSyncApi.links()).find((link) => link.id === linkId)?.state ===
            DataSyncLinkState.AwaitingAccess;

        if (!actions.mounted.current) return;
        if (requested) actions.setNotice(t("dataSync.wizard.requested", { name }));
        else navigate(dataSyncReviewRoute(linkId));
      },
      refresh: ["dataSync"],
    });

  /**
   * A code only works while this device shares its definitions and remote access is on (spec
   * §7.2.3): where either is off, it offers to turn them on first, then shows the code.
   */
  const createCode = () => {
    if (!onCreateCode) return;
    if (!mustTurnOn) {
      onCreateCode();

      return;
    }
    actions.confirm({
      title: t("dataSync.sharing.onTitle"),
      description: t("dataSync.invitation.needsSharingFirst", { name }),
      warning: turnsOnWarning(t, own),
      action: async () => {
        await turnOnSharing();
        if (actions.mounted.current) onCreateCode();
      },
      refresh: ["dataSync", "sharing"],
    });
  };

  const receiveTarget = receiveToggleTarget(editor);
  const receiveAllowed =
    allowed(receiveTarget) && (receiveTarget !== "off" || peer.linkId !== undefined);
  const receiveLabel = [
    editor.receive === "none"
      ? t("dataSync.arrow.receive.off", { name })
      : t(`federation.map.direction.sync.in.${editor.receive}`, { name }),
    editor.badge ? t(`dataSync.mode.${editor.badge}`) : undefined,
  ]
    .filter(Boolean)
    .join(", ");
  const readLabel =
    editor.read === "active"
      ? t("federation.map.direction.sync.out.active", { name })
      : t("dataSync.arrow.read.off", { name });
  const copyOnceOffered =
    editor.mode === "off" &&
    (peer.state === undefined || peer.state === DataSyncLinkState.Stopped) &&
    peer.outcome !== "awaitingApproval";
  const manageNote = !canManage && (["follow", "twoWay"] as const).some((mode) => !allowed(mode));

  const receiveArrow = (
    <button
      aria-disabled={receiveAllowed ? undefined : "true"}
      aria-label={receiveLabel}
      aria-pressed={editor.receiving}
      className={`group flex w-full items-center gap-2 rounded-lg px-1 py-1.5 outline-none transition focus-visible:ring-2 focus-visible:ring-focus disabled:cursor-not-allowed disabled:opacity-60 ${
        horizontal ? "flex-row" : "flex-col"
      } hover:bg-default-100`}
      data-status={editor.receive}
      data-testid="data-sync-arrow-receive"
      disabled={actions.busy}
      title={receiveLabel}
      type="button"
      onClick={() => receiveAllowed && setMode(receiveTarget)}
      onKeyDown={activate(() => receiveAllowed && setMode(receiveTarget))}
      onKeyUp={swallowSpaceUp}
    >
      <Arrow direction={horizontal ? "left" : "down"} status={editor.receive} />
      <span className={`${ARROW_TEXT_SIZE} shrink-0 font-medium ${syncText}`}>
        {t("dataSync.arrow.receive.label")}
      </span>
    </button>
  );

  const readArrow = (
    <button
      aria-disabled={editor.read === "active" ? undefined : "true"}
      aria-label={readLabel}
      aria-pressed={editor.read === "active"}
      className={`group flex w-full items-center gap-2 rounded-lg px-1 py-1.5 outline-none transition focus-visible:ring-2 focus-visible:ring-focus ${
        horizontal ? "flex-row" : "flex-col"
      } ${editor.read === "active" ? "hover:bg-default-100" : "cursor-default"}`}
      data-status={editor.read}
      data-testid="data-sync-arrow-read"
      disabled={actions.busy && editor.read === "active"}
      title={editor.read === "active" ? readLabel : t("dataSync.arrow.read.offHint", { name })}
      type="button"
      onClick={() => editor.read === "active" && stopReading()}
      onKeyDown={activate(() => editor.read === "active" && stopReading())}
      onKeyUp={swallowSpaceUp}
    >
      <span className={`${ARROW_TEXT_SIZE} shrink-0 font-medium ${syncText}`}>
        {t("dataSync.arrow.read.label")}
      </span>
      <Arrow direction={horizontal ? "right" : "up"} status={editor.read} />
    </button>
  );

  return (
    <div
      ref={ref}
      className="space-y-3"
      data-orientation={horizontal ? "horizontal" : "vertical"}
      data-testid="data-sync-rule-drawing"
    >
      {/* What the drawing shows, for a screen reader: one item per direction. */}
      <ul className="sr-only" data-testid="data-sync-rule-list">
        <li>{receiveLabel}</li>
        <li>
          {readLabel}
          {editor.read === "active" && peer.peerKinds?.length
            ? `: ${peer.peerKinds.map((kind) => t(`dataSync.kind.${kind}`, { defaultValue: kind })).join(", ")}`
            : ""}
        </li>
      </ul>
      <div
        className={
          horizontal
            ? "grid grid-cols-[minmax(0,7rem)_minmax(0,1fr)_minmax(0,7rem)] items-center gap-2"
            : "flex flex-col items-stretch gap-2"
        }
      >
        {horizontal ? (
          <DeviceBox self subtitle={t("dataSync.thisDevice")} title={selfName} />
        ) : (
          <DeviceBox subtitle={t("dataSync.otherDevice")} title={name} />
        )}
        <div className="min-w-0 space-y-2">
          <div className="space-y-1">
            {horizontal ? receiveArrow : readArrow}
            {horizontal ? (
              <ReceiveControls
                actions={actions}
                editor={editor}
                kinds={kinds}
                modeAllowed={allowed}
                onKind={setKinds}
                onMode={setMode}
              />
            ) : (
              <PeerKinds kinds={editor.read === "active" ? peer.peerKinds : undefined} />
            )}
          </div>
          <div className="space-y-1">
            {horizontal ? readArrow : receiveArrow}
            {horizontal ? (
              <PeerKinds kinds={editor.read === "active" ? peer.peerKinds : undefined} />
            ) : (
              <ReceiveControls
                actions={actions}
                editor={editor}
                kinds={kinds}
                modeAllowed={allowed}
                onKind={setKinds}
                onMode={setMode}
              />
            )}
          </div>
        </div>
        {horizontal ? (
          <DeviceBox subtitle={t("dataSync.otherDevice")} title={name} />
        ) : (
          <DeviceBox self subtitle={t("dataSync.thisDevice")} title={selfName} />
        )}
      </div>

      {editor.read === "none" && canManage && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
          <span>{t("dataSync.arrow.read.offHint", { name })}</span>
          {editor.mode !== "twoWay" && (
            <button
              className={smallButtonClass}
              disabled={actions.busy}
              type="button"
              onClick={() => setMode("twoWay")}
            >
              {t("dataSync.mode.twoWay")}
            </button>
          )}
          {onCreateCode && (
            <button
              className={smallButtonClass}
              data-testid="data-sync-create-code-for"
              disabled={actions.busy}
              type="button"
              onClick={createCode}
            >
              {t("dataSync.invitation.createFor", { name })}
            </button>
          )}
        </div>
      )}

      <fieldset className="space-y-1.5">
        <legend className="sr-only">{t("dataSync.mode.legend", { name })}</legend>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5">
          {(["off", "follow", "twoWay"] as const).map((mode) => (
            <label
              key={mode}
              className={`inline-flex items-center gap-1.5 text-sm ${allowed(mode) ? "cursor-pointer" : "cursor-not-allowed opacity-60"}`}
            >
              <input
                checked={editor.mode === mode}
                className="accent-secondary"
                data-testid={`data-sync-mode-${mode}`}
                disabled={actions.busy || !allowed(mode)}
                name={radioName}
                type="radio"
                value={mode}
                onChange={() => setMode(mode)}
              />
              {t(`dataSync.mode.${mode}`)}
            </label>
          ))}
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
      </fieldset>

      <StatusBlock
        actions={actions}
        canManage={canManage}
        linkToPage={linkToPage}
        notes={notes}
        now={now}
        peer={peer}
        status={status}
        onStop={() => setMode("off")}
        onStopReading={stopReading}
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
 * One arrow: solid when working, dashed while it waits, a faint dotted outline with a "+" when
 * off. Drawn in the device map's colour for data sync, pointing to the device that receives.
 */
function Arrow({
  direction,
  status,
}: {
  direction: "left" | "right" | "up" | "down";
  status: "active" | "pending" | "none";
}) {
  const vertical = direction === "up" || direction === "down";
  const lineStyle =
    status === "active"
      ? "border-solid"
      : status === "pending"
        ? "border-dashed"
        : "border-dotted opacity-50";
  const color = status === "none" ? "border-default-400" : "border-secondary";
  const head = (
    <svg
      aria-hidden
      className={`shrink-0 ${status === "none" ? "fill-default-400 opacity-50" : edgeStyles.sync.fill}`}
      height={10}
      viewBox="0 0 10 10"
      width={10}
    >
      <path
        d={
          direction === "left"
            ? "M10 0.5 L0.5 5 L10 9.5 L7.5 5 z"
            : direction === "right"
              ? "M0 0.5 L9.5 5 L0 9.5 L2.5 5 z"
              : direction === "up"
                ? "M0.5 10 L5 0.5 L9.5 10 L5 7.5 z"
                : "M0.5 0 L5 9.5 L9.5 0 L5 2.5 z"
        }
      />
    </svg>
  );
  const shaft = (
    <span
      className={`relative ${vertical ? "h-8 border-l-2" : "h-0 flex-1 border-t-2"} ${lineStyle} ${color}`}
    >
      {status === "none" && (
        <span
          aria-hidden
          className="absolute left-1/2 top-1/2 flex h-4 w-4 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full border border-default-400 bg-content1 text-[11px] leading-none text-default-500"
        >
          +
        </span>
      )}
      {status === "active" && (
        <svg
          aria-hidden
          className="absolute left-1/2 top-1/2 h-4 w-4 -translate-x-1/2 -translate-y-1/2"
          viewBox="-12 -12 24 24"
        >
          <circle className={`fill-content1 ${edgeStyles.sync.stroke}`} r={10} strokeWidth={1.5} />
          <KindBadgeGlyph className={edgeStyles.sync.stroke} kind="sync" />
        </svg>
      )}
    </span>
  );

  return (
    <span
      aria-hidden
      className={`flex min-w-0 items-center ${vertical ? "flex-col" : "flex-1 flex-row"}`}
      data-arrow={direction}
    >
      {direction === "left" || direction === "up" ? head : null}
      {shaft}
      {direction === "right" || direction === "down" ? head : null}
    </span>
  );
}

/** The mode badge and the kind chips on the receive arrow. */
function ReceiveControls({
  editor,
  kinds,
  actions,
  modeAllowed,
  onMode,
  onKind,
}: {
  editor: LinkEditor;
  kinds: string[];
  actions: DataSyncPanelActions;
  modeAllowed: (mode: ModeName) => boolean;
  onMode: (mode: ModeName) => void;
  onKind: (kind: string) => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap items-center gap-1.5 px-1">
      {editor.badge && (
        <ModeBadgeMenu
          allowed={modeAllowed}
          busy={actions.busy}
          value={editor.badge}
          onChange={onMode}
        />
      )}
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
            onClick={() => !locked && onKind(kind)}
          >
            {t(`dataSync.kind.${kind}`, { defaultValue: kind })}
          </button>
        );
      })}
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
    <div className="flex flex-wrap items-center gap-1.5 px-1" data-testid="data-sync-peer-kinds">
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

/**
 * The receive arrow's mode, as a small menu: "both ways" or "receive only". Choosing one runs
 * exactly what the mode buttons run.
 */
function ModeBadgeMenu({
  value,
  busy,
  allowed,
  onChange,
}: {
  value: "follow" | "twoWay";
  busy: boolean;
  allowed: (mode: ModeName) => boolean;
  onChange: (mode: ModeName) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLSpanElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLSpanElement>(null);
  const menuId = useId();
  // Escape closes the menu, and only the menu: the details around it stay open.
  const menuKeys = useMenuKeyboard(open, menu, trigger, () => setOpen(false), root);

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };

    document.addEventListener("pointerdown", close, true);

    return () => document.removeEventListener("pointerdown", close, true);
  }, [open]);

  const choose = (mode: ModeName) => {
    if (!allowed(mode)) return;
    setOpen(false);
    trigger.current?.focus();
    onChange(mode);
  };

  return (
    <span ref={root} className="relative inline-flex">
      <button
        ref={trigger}
        aria-controls={open ? menuId : undefined}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={t("dataSync.mode.badgeLabel", { mode: t(`dataSync.mode.${value}`) })}
        className={`inline-flex items-center gap-1 rounded-full border border-secondary bg-content1 px-2 py-0.5 text-[11px] font-medium ${syncText} hover:bg-secondary/10 disabled:opacity-50`}
        data-mode={value}
        data-testid="data-sync-mode-badge"
        disabled={busy}
        id={`${menuId}-button`}
        type="button"
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (!open && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        {t(`dataSync.mode.short.${value}`)}
        <span aria-hidden>▾</span>
      </button>
      {open && (
        <span
          ref={menu}
          aria-labelledby={`${menuId}-button`}
          className="absolute left-0 top-full z-20 mt-1 flex min-w-40 flex-col rounded-lg border border-default-200 bg-content1 p-1 shadow-lg"
          id={menuId}
          role="menu"
          tabIndex={-1}
          onKeyDown={menuKeys}
        >
          {(["twoWay", "follow"] as const).map((mode) => (
            <button
              key={mode}
              aria-checked={value === mode}
              aria-disabled={allowed(mode) ? undefined : "true"}
              className="rounded-md px-2 py-1.5 text-left text-xs outline-none hover:bg-default-100 focus-visible:bg-default-100 focus-visible:ring-2 focus-visible:ring-focus aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
              data-menu-mode={mode}
              role="menuitemradio"
              tabIndex={-1}
              type="button"
              onClick={() => choose(mode)}
            >
              <span aria-hidden>{value === mode ? "● " : "○ "}</span>
              {t(`dataSync.mode.${mode}`)}
            </button>
          ))}
        </span>
      )}
    </span>
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
  onStopReading,
}: {
  peer: SyncPeer;
  status: StatusLine;
  notes: StatusLine[];
  actions: DataSyncPanelActions;
  canManage: boolean;
  linkToPage: boolean;
  now?: number;
  onStop: () => void;
  /** Stops the other device reading this one, with its own confirmation. */
  onStopReading: () => void;
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
    case "Paused.MassDeletion":
    case "Paused.KindEmptied":
      statusActions.push(
        resume(DataSyncResumeAction.ApplyAsUsual, t("dataSync.pause.applyAsUsual")),
        resume(DataSyncResumeAction.ReviewDeletions, t("dataSync.pause.reviewDeletions")),
        stop,
      );
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
    case "Paused.TooManyDecisions":
      statusActions.push(inbox, resume(DataSyncResumeAction.Resume, t("dataSync.pause.resume")));
      break;
    case "AccessRevoked":
      // The same line says the other device turned sharing off, where asking again cannot help:
      // it answers the moment sharing is back on.
      if (peer.state === DataSyncLinkState.AccessRevoked)
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
    peer.state === DataSyncLinkState.Active && linkId !== undefined && status.code !== "Failed";
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
              <span className="text-default-500">{t("dataSync.link.decideThere", { name })}</span>
            ))}
        </div>
      ))}
      {stillReadsWhileOff(peer) && (
        // Receiving is off, reading is not: the choice the Off confirmation named, kept here.
        <div className="flex flex-wrap items-center gap-2 text-xs" data-note="StillReads">
          <span className="text-default-500">{t("dataSync.off.stillReads", { name })}</span>
          {button(t("dataSync.off.alsoStop"), onStopReading, "data-sync-also-stop-reading")}
        </div>
      )}
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
    case DataSyncPauseReason.MassDeletion:
    case DataSyncPauseReason.KindEmptied:
      return t("dataSync.pause.deletionsHint", { name: peer.name });
    case DataSyncPauseReason.PeerReset:
      return pauseDetail(peer.pausedDetail).restored
        ? t("dataSync.pause.restoredHint", { name: peer.name })
        : t("dataSync.pause.resetHint", { name: peer.name });
    case DataSyncPauseReason.PeerIdentityDuplicated:
      return t("dataSync.pause.duplicatedHint", { name: peer.name });
    case DataSyncPauseReason.TooManyDecisions:
      return t("dataSync.pause.tooManyHint");
    default:
      return undefined;
  }
};
