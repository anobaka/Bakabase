"use client";

import type { ClientSwitcher } from "@/core/clientApi";
import type { ManagedServersView } from "@/features/federation/types";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCheck,
  AiOutlineCloudServer,
  AiOutlineDesktop,
  AiOutlineDown,
  AiOutlineSetting,
} from "react-icons/ai";

import { clientApi, LOCAL_SWITCHER_TARGET } from "@/core/clientApi";
import { managedServerApi } from "@/features/federation/serverApi";
import {
  devicesRoute,
  openConsoleTarget,
  openLocalView,
  openManagedServer,
} from "@/features/federation/switching";
import { FederationError } from "@/features/federation/transport";
import { ClientMode, ManagedServerState, ManagedServerStateLabel } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

export interface SwitcherEntry {
  id: string;
  name: string;
  isLocal: boolean;
  isCurrent: boolean;
  /**
   * Managed servers only, once checked: probed from this device's own window, or as the
   * console's relay reports it. Absent reads as not checked.
   */
  state?: ManagedServerState;
}

interface Props {
  collapsed: boolean;
}

/** "Manage devices…" lands on the list of managed servers, on this device's own page. */
const MANAGE_DEVICES_ROUTE = devicesRoute("servers");

/**
 * The top of the menu: which server this window is showing, and the way to another.
 *
 * Three answers to "what can this window switch to", one per flavour:
 * - this device's own window (all-in-one, sitting at it): itself plus every server it
 *   manages, from `/federation/local/servers`;
 * - the console (the desktop app showing a managed server): whatever the relay lists at
 *   `/client/switcher` — the relay, not the server being shown, knows the way back;
 * - anything else — an ordinary browser on another device, or a flavour not yet
 *   identified — keeps the plain brand link. None of them has anywhere
 *   to switch to, and a switcher offering nothing would only suggest otherwise.
 */
const ServerSwitcher: React.FC<Props> = ({ collapsed }) => {
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const isLocal = useRemoteAccessStore((state) => state.isLocal);
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const clientHost = useRemoteAccessStore((state) => state.clientHost);

  if (initialized && clientMode === ClientMode.PureClient && clientHost === "console") {
    return <ConsoleSwitcher collapsed={collapsed} />;
  }
  if (initialized && isLocal && clientMode !== ClientMode.PureClient) {
    return <LocalSwitcher collapsed={collapsed} />;
  }

  return <BrandLink collapsed={collapsed} />;
};

const BrandLink: React.FC<Props> = ({ collapsed }) => (
  <Link to="/">{collapsed ? "B" : "Bakabase"}</Link>
);

/** This device's own window: itself, then the servers it manages. */
const LocalSwitcher: React.FC<Props> = ({ collapsed }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const [view, setView] = useState<ManagedServersView>();
  const [listFailed, setListFailed] = useState(false);
  const generation = useRef(0);

  const load = useCallback(async (probe: boolean) => {
    const run = ++generation.current;

    try {
      const fresh = await managedServerApi.list(probe);

      if (run === generation.current) {
        setView(fresh);
        setListFailed(false);
      }
    } catch {
      // Said inside the menu, where the missing servers would be; this device is still
      // listed, and the devices page explains the failure in full.
      if (run === generation.current) setListFailed(true);
    }
  }, []);

  useEffect(() => {
    void load(false);

    return () => {
      generation.current += 1;
    };
  }, [load]);

  // A headless server has nothing to switch to; do not dress its brand link up as a menu.
  if (view && !view.available) return <BrandLink collapsed={collapsed} />;

  const localName = serverName || t<string>("federation.thisDevice");
  const entries: SwitcherEntry[] = [
    { id: LOCAL_SWITCHER_TARGET, name: localName, isLocal: true, isCurrent: true },
    ...(view?.servers ?? []).map((server) => ({
      id: server.serverId,
      name: server.name || server.address,
      isLocal: false,
      isCurrent: false,
      state: server.state,
    })),
  ];

  return (
    <SwitcherView
      collapsed={collapsed}
      currentName={localName}
      entries={entries}
      listError={listFailed ? t<string>("federation.switcher.listFailed") : undefined}
      managing={false}
      onManageDevices={() => navigate(MANAGE_DEVICES_ROUTE)}
      // Opening is the moment the states matter, and the probe is bounded server-side.
      onOpen={() => void load(true)}
      onSelect={(entry) => openManagedServer(entry.id)}
    />
  );
};

/** The desktop app's window while it shows a server it manages. */
const ConsoleSwitcher: React.FC<Props> = ({ collapsed }) => {
  const { t } = useTranslation();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const localName = useRemoteAccessStore((state) => state.localName);
  const [switcher, setSwitcher] = useState<ClientSwitcher>();
  const [listFailed, setListFailed] = useState(false);

  const load = useCallback(async () => {
    try {
      setSwitcher(await clientApi.switcher.list());
      setListFailed(false);
    } catch {
      // Said inside the menu; the fallback below still offers the way back to this device.
      setListFailed(true);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const current = switcher?.targets.find((target) => target.isCurrent);
  const currentName = current?.name || serverName || t<string>("federation.switcher.unnamed");
  // Without the list, the one destination that is always valid is this device.
  const entries: SwitcherEntry[] = switcher?.targets.length
    ? switcher.targets.map((target) => ({
        id: target.id,
        name: target.name,
        isLocal: target.isLocal,
        isCurrent: target.isCurrent,
        // The relay says how each server was when last checked, as this device's own
        // window does; a relay that predates it says nothing, which reads as not checked.
        state: target.isLocal ? undefined : target.state,
      }))
    : [
        {
          id: LOCAL_SWITCHER_TARGET,
          name: localName || t<string>("federation.thisDevice"),
          isLocal: true,
          isCurrent: false,
        },
        { id: "current", name: currentName, isLocal: false, isCurrent: true },
      ];

  return (
    <SwitcherView
      managing
      collapsed={collapsed}
      currentName={currentName}
      entries={entries}
      listError={listFailed ? t<string>("federation.switcher.listFailed") : undefined}
      onManageDevices={() => openLocalView(MANAGE_DEVICES_ROUTE)}
      onOpen={() => void load()}
      onSelect={(entry) => openConsoleTarget(entry.id)}
    />
  );
};

const stateDotClass: Record<ManagedServerState, string> = {
  [ManagedServerState.Unknown]: "border border-default-300",
  [ManagedServerState.Online]: "bg-success",
  [ManagedServerState.Offline]: "bg-default-300",
  [ManagedServerState.Revoked]: "bg-danger",
  [ManagedServerState.WrongServer]: "bg-warning",
};

/** States worth a word next to the name: the entry would not show that server if opened. */
const labelledStates = new Set<ManagedServerState>([
  ManagedServerState.Offline,
  ManagedServerState.Revoked,
  ManagedServerState.WrongServer,
]);

/**
 * The state an entry's dot shows. In the console the list comes from this device's relay
 * and the bundle from the server being shown — two versions that need not match — so a
 * state this bundle does not know reads as not checked, like one never reported.
 */
const dotState = (state?: ManagedServerState): ManagedServerState =>
  typeof state === "number" && state in stateDotClass ? state : ManagedServerState.Unknown;

const itemSelector = '[role="menuitem"]:not([disabled])';

export const SwitcherView: React.FC<{
  collapsed: boolean;
  currentName: string;
  /** The window shows another server: say so wherever the name appears. */
  managing: boolean;
  entries: SwitcherEntry[];
  /** Set when the list of destinations could not be read; shown in the menu, with a retry. */
  listError?: string;
  /** Called on every opening; also the retry after {@link listError}. */
  onOpen: () => void;
  /** Resolves once navigation has been asked for; rejects with what went wrong. */
  onSelect: (entry: SwitcherEntry) => Promise<void>;
  onManageDevices: () => unknown;
}> = ({
  collapsed,
  currentName,
  managing,
  entries,
  listError,
  onOpen,
  onSelect,
  onManageDevices,
}) => {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState<string>();
  const [error, setError] = useState<string>();
  const wrapper = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const mounted = useRef(true);
  /** Which end of the menu takes focus when it opens: ArrowUp on the button starts at the last. */
  const focusFrom = useRef<"first" | "last">("first");

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);

  const close = useCallback((refocus: boolean) => {
    setOpen(false);
    if (refocus) trigger.current?.focus();
  }, []);

  const show = (from: "first" | "last") => {
    focusFrom.current = from;
    setError(undefined);
    setOpen(true);
    onOpen();
  };

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (!wrapper.current?.contains(event.target as Node)) close(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        close(true);
      }
    };

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    const items = menu.current?.querySelectorAll<HTMLElement>(itemSelector);

    (focusFrom.current === "last" ? items?.[items.length - 1] : items?.[0])?.focus();

    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, close]);

  const describe = (cause: unknown) => {
    const key =
      cause instanceof FederationError
        ? `federation.error.${cause.code}`
        : "federation.switcher.openFailed";
    const known = typeof i18n?.exists === "function" && i18n.exists(key);

    return known ? t(key) : t("federation.switcher.openFailed");
  };

  const select = async (entry: SwitcherEntry) => {
    if (entry.isCurrent) {
      close(true);

      return;
    }
    setBusyId(entry.id);
    setError(undefined);
    try {
      await onSelect(entry);
      // Navigation replaces this page; the busy state is left on so nothing else is
      // clicked in the moment before it goes.
    } catch (cause) {
      if (!mounted.current) return;
      setBusyId(undefined);
      setError(describe(cause));
    }
  };

  const manageDevices = async () => {
    setError(undefined);
    try {
      await onManageDevices();
      if (mounted.current) setOpen(false);
    } catch (cause) {
      if (mounted.current) setError(describe(cause));
    }
  };

  /** Arrow keys walk the items, Home and End jump to either end — the WAI-ARIA menu pattern. */
  const onMenuKeyDown = (event: React.KeyboardEvent) => {
    const items = Array.from(menu.current?.querySelectorAll<HTMLElement>(itemSelector) ?? []);

    if (!items.length) return;
    const index = items.indexOf(document.activeElement as HTMLElement);
    let next: number;

    switch (event.key) {
      case "ArrowDown":
        next = index + 1;
        break;
      case "ArrowUp":
        next = index < 0 ? items.length - 1 : index - 1;
        break;
      case "Home":
        next = 0;
        break;
      case "End":
        next = items.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    items[(next + items.length) % items.length]?.focus();
  };

  const busy = busyId !== undefined;
  const label = managing
    ? t("federation.switcher.managingName", { name: currentName })
    : currentName;
  const CurrentIcon = managing ? AiOutlineCloudServer : AiOutlineDesktop;
  const itemClass =
    "flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-sm outline-none hover:bg-default-100 focus-visible:bg-default-100 focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <div
      ref={wrapper}
      className="relative font-sans"
      data-testid="server-switcher"
      // Tabbing out of an open menu closes it, as clicking outside does. Checked after the
      // focus has moved, so the Tab still lands where it was going. Not while a switch is
      // under way: disabling the focused item drops its focus, and the menu has to stay to
      // say how the switch went.
      onBlur={(event) => {
        if (open && !busy && !wrapper.current?.contains(event.relatedTarget as Node | null))
          close(false);
      }}
    >
      <button
        ref={trigger}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={t("federation.switcher.label", { name: label })}
        className={`flex items-center rounded-lg text-left outline-none transition hover:bg-default-100 focus-visible:ring-2 focus-visible:ring-primary/50 ${
          collapsed ? "mx-auto h-9 w-9 justify-center" : "w-full gap-2 px-2 py-1"
        } ${managing ? "bg-warning/10 text-warning-700 ring-1 ring-warning/50 dark:text-warning" : ""}`}
        title={label}
        type="button"
        onClick={() => (open ? close(false) : show("first"))}
        onKeyDown={(event) => {
          if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
          event.preventDefault();
          const from = event.key === "ArrowUp" ? "last" : "first";

          if (!open) {
            show(from);
          } else {
            const items = menu.current?.querySelectorAll<HTMLElement>(itemSelector);

            (from === "last" ? items?.[items.length - 1] : items?.[0])?.focus();
          }
        }}
      >
        {collapsed ? (
          <span aria-hidden className="text-base font-semibold leading-none">
            {Array.from(currentName)[0]?.toUpperCase() ?? "B"}
          </span>
        ) : (
          <>
            <CurrentIcon aria-hidden className="shrink-0 text-lg" />
            <span className="min-w-0 flex-1">
              {managing && (
                <span className="block text-[11px] font-medium uppercase leading-4 tracking-wide">
                  {t("federation.switcher.managing")}
                </span>
              )}
              <span className="block truncate text-sm font-medium leading-5">{currentName}</span>
            </span>
            <AiOutlineDown aria-hidden className="shrink-0 text-xs opacity-60" />
          </>
        )}
      </button>
      {open && (
        <div
          ref={menu}
          aria-label={t("federation.switcher.menu")}
          className={`absolute z-50 w-64 rounded-xl border border-default-200 bg-content1 p-1 text-left text-foreground shadow-lg outline-none ${
            collapsed ? "left-full top-0 ml-3" : "left-0 top-full mt-1"
          }`}
          role="menu"
          // Focusable itself so a click on its padding keeps focus inside, and open.
          tabIndex={-1}
          onKeyDown={onMenuKeyDown}
        >
          {collapsed && (
            // The collapsed button shows only an initial; the open menu says the rest.
            <p className="truncate px-2 pb-1 pt-1.5 text-xs font-medium">{label}</p>
          )}
          <p className="px-2 pb-1 pt-1.5 text-[11px] font-medium uppercase tracking-wide text-default-400">
            {t("federation.switcher.switchTo")}
          </p>
          {entries.map((entry) => (
            <button
              key={entry.id}
              aria-current={entry.isCurrent ? "true" : undefined}
              className={itemClass}
              disabled={busy}
              role="menuitem"
              type="button"
              onClick={() => void select(entry)}
            >
              {entry.isLocal ? (
                <AiOutlineDesktop aria-hidden className="shrink-0" />
              ) : (
                <span
                  aria-hidden
                  className={`ml-[3px] mr-[3px] h-2 w-2 shrink-0 rounded-full ${
                    stateDotClass[dotState(entry.state)]
                  }`}
                  data-state={ManagedServerStateLabel[dotState(entry.state)]}
                  data-testid="server-state-dot"
                />
              )}
              <span className="min-w-0 flex-1 truncate">{entry.name}</span>
              {entry.isLocal && (
                <span className="shrink-0 text-xs text-default-400">
                  {t("federation.thisDevice")}
                </span>
              )}
              {!entry.isLocal && entry.state !== undefined && labelledStates.has(entry.state) && (
                <span className="shrink-0 text-xs text-default-400">
                  {t(`federation.servers.state.${entry.state}`)}
                </span>
              )}
              {busyId === entry.id && (
                <span className="shrink-0 text-xs text-default-400">
                  {t("federation.switcher.opening")}
                </span>
              )}
              {entry.isCurrent && <AiOutlineCheck aria-hidden className="shrink-0 text-primary" />}
            </button>
          ))}
          {entries.length === 1 && !listError && (
            <p className="px-2 py-1 text-xs text-default-400">{t("federation.switcher.empty")}</p>
          )}
          {listError && (
            <div className="px-2 py-1" data-testid="server-switcher-list-error">
              <p className="text-xs text-danger" role="alert">
                {listError}
              </p>
              <button
                className={`${itemClass} mt-1 !px-0 !py-0.5 text-xs text-primary`}
                disabled={busy}
                role="menuitem"
                type="button"
                onClick={onOpen}
              >
                {t("federation.retry")}
              </button>
            </div>
          )}
          <div className="my-1 h-px bg-default-200" role="separator" />
          <button
            className={itemClass}
            disabled={busy}
            role="menuitem"
            type="button"
            onClick={() => void manageDevices()}
          >
            <AiOutlineSetting aria-hidden className="shrink-0" />
            <span className="min-w-0 flex-1 truncate">
              {t("federation.switcher.manageDevices")}
            </span>
          </button>
          {error && (
            <p className="px-2 py-1 text-xs text-danger" role="alert">
              {error}
            </p>
          )}
        </div>
      )}
    </div>
  );
};

ServerSwitcher.displayName = "ServerSwitcher";

export default ServerSwitcher;
