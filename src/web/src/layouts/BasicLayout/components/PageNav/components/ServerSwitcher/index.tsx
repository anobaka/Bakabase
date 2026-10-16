"use client";

import type { ClientSwitcher } from "@/core/clientApi";
import type { ManagedServersView } from "@/features/federation/types";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
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
  CONFIGURATION_ROUTE,
  devicesRoute,
  openConsoleTarget,
  openLocalView,
  openManagedServer,
} from "@/features/federation/switching";
import { FederationError } from "@/features/federation/transport";
import { deviceSwitchRoute } from "@/features/federation/deviceSwitchRoute";
import { ClientMode, ManagedServerState, ManagedServerStateLabel } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { Dropdown, DropdownItem, DropdownMenu, DropdownTrigger } from "@/components/bakaui";

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
 * The top of the menu: the dashboard brand link, then the device this window shows.
 *
 * Three answers to "what can this window switch to", one per flavour:
 * - this device's own window (all-in-one, sitting at it): itself plus every server it
 *   manages, from `/federation/local/servers`;
 * - the console (the desktop app showing a managed server): whatever the relay lists at
 *   `/client/switcher` — the relay, not the server being shown, knows the way back;
 * - anything else — an ordinary browser on another device, or a flavour not yet
 *   identified — keeps only the brand link. None of them has anywhere to switch to.
 */
const ServerSwitcher: React.FC<Props> = ({ collapsed }) => {
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const isLocal = useRemoteAccessStore((state) => state.isLocal);
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const clientHost = useRemoteAccessStore((state) => state.clientHost);

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <BrandLink collapsed={collapsed} />
      {initialized && clientMode === ClientMode.PureClient && clientHost === "console" ? (
        <ConsoleSwitcher collapsed={collapsed} />
      ) : initialized && isLocal && clientMode !== ClientMode.PureClient ? (
        <LocalSwitcher collapsed={collapsed} />
      ) : null}
    </div>
  );
};

const BrandLink: React.FC<Props> = ({ collapsed }) => (
  <Link
    aria-label="Bakabase"
    className="block rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
    to="/"
  >
    {collapsed ? "B" : "Bakabase"}
  </Link>
);

/** This device's own window: itself, then the servers it manages. */
const LocalSwitcher: React.FC<Props> = ({ collapsed }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { pathname } = useLocation();
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

  // A headless server has nothing to switch to; its dashboard brand remains above.
  if (view && (!view.available || view.canOpenDesktopView === false)) return null;

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
      controllerName={localName}
      currentName={localName}
      entries={entries}
      listError={listFailed ? t<string>("federation.switcher.listFailed") : undefined}
      managing={false}
      onControllerSettings={() => navigate(CONFIGURATION_ROUTE)}
      onManageDevices={() => navigate(MANAGE_DEVICES_ROUTE)}
      onOpen={() => void load(true)}
      onSelect={(entry) => openManagedServer(entry.id, deviceSwitchRoute(pathname))}
    />
  );
};

/** The desktop app's window while it shows a server it manages. */
const ConsoleSwitcher: React.FC<Props> = ({ collapsed }) => {
  const { t } = useTranslation();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const localName = useRemoteAccessStore((state) => state.localName);
  const navigate = useNavigate();
  const { pathname } = useLocation();
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
      controllerName={entries.find((entry) => entry.isLocal)?.name || localName}
      currentName={currentName}
      entries={entries}
      listError={listFailed ? t<string>("federation.switcher.listFailed") : undefined}
      onControllerSettings={() => openLocalView(CONFIGURATION_ROUTE)}
      onManageDevices={() => openLocalView(MANAGE_DEVICES_ROUTE)}
      onOpen={() => void load()}
      onPathMapping={() => navigate("/client-path-mapping")}
      onSelect={(entry) => openConsoleTarget(entry.id, deviceSwitchRoute(pathname))}
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

export const SwitcherView: React.FC<{
  collapsed: boolean;
  currentName: string;
  controllerName?: string;
  /** The window shows another server: say so wherever the name appears. */
  managing: boolean;
  entries: SwitcherEntry[];
  listError?: string;
  onOpen: () => void;
  onSelect: (entry: SwitcherEntry) => Promise<void>;
  onManageDevices: () => unknown;
  onControllerSettings: () => unknown;
  /** Only relays can edit the controlling app's mapping to the device currently shown. */
  onPathMapping?: () => unknown;
}> = ({
  collapsed,
  currentName,
  controllerName,
  managing,
  entries,
  listError,
  onOpen,
  onSelect,
  onManageDevices,
  onControllerSettings,
  onPathMapping,
}) => {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [busyId, setBusyId] = useState<string>();
  const [error, setError] = useState<string>();
  const [settingsError, setSettingsError] = useState<string>();
  const [settingsBusy, setSettingsBusy] = useState(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);

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
      setOpen(false);

      return;
    }
    setBusyId(entry.id);
    setError(undefined);
    try {
      await onSelect(entry);
      // Navigation replaces this page. Keep the selected entry busy until it does.
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

  const openSettings = async (pathMapping: boolean) => {
    setSettingsBusy(true);
    setSettingsError(undefined);
    try {
      await (pathMapping ? onPathMapping?.() : onControllerSettings());
      if (mounted.current) setSettingsOpen(false);
    } catch {
      if (mounted.current) setSettingsError(t("federation.switcher.controllerSettingsFailed"));
    } finally {
      if (mounted.current) setSettingsBusy(false);
    }
  };

  const busy = busyId !== undefined;
  const label = managing
    ? t("federation.switcher.managingName", { name: currentName })
    : currentName;
  const controllerLabel = controllerName
    ? t("federation.switcher.controllerSettingsNamed", { name: controllerName })
    : t("federation.switcher.controllerSettings");
  const CurrentIcon = managing ? AiOutlineCloudServer : AiOutlineDesktop;
  const settingsTrigger = (
    <button
      aria-label={controllerLabel}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-default-500 outline-none transition hover:bg-default-100 hover:text-foreground focus-visible:ring-2 focus-visible:ring-primary/50 disabled:opacity-50"
      disabled={busy || settingsBusy}
      title={controllerLabel}
      type="button"
      onClick={onPathMapping ? undefined : () => void openSettings(false)}
    >
      <AiOutlineSetting aria-hidden className="text-lg" />
    </button>
  );

  return (
    <div
      className={`flex min-w-0 items-center gap-1 font-sans ${collapsed ? "flex-col" : ""}`}
      data-testid="server-switcher"
    >
      <Dropdown
        disableAnimation
        closeOnSelect={false}
        isOpen={open}
        placement={collapsed ? "right-start" : "bottom-start"}
        onOpenChange={(next) => {
          if (busy && !next) return;
          setOpen(next);
          if (next) {
            setError(undefined);
            onOpen();
          }
        }}
      >
        <DropdownTrigger>
          <button
            aria-label={t("federation.switcher.label", { name: label })}
            className={`flex min-w-0 items-center rounded-lg text-left outline-none transition hover:bg-default-100 focus-visible:ring-2 focus-visible:ring-primary/50 ${
              collapsed ? "h-9 w-9 justify-center" : "flex-1 gap-2 px-2 py-1"
            } ${managing ? "bg-warning/10 text-warning-700 ring-1 ring-warning/50 dark:text-warning" : ""}`}
            title={label}
            type="button"
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
                    <span className="block text-[11px] font-medium leading-4">
                      {t("federation.switcher.managing")}
                    </span>
                  )}
                  <span className="block truncate text-sm font-medium leading-5">
                    {currentName}
                  </span>
                </span>
                <AiOutlineDown aria-hidden className="shrink-0 text-xs opacity-60" />
              </>
            )}
          </button>
        </DropdownTrigger>
        <DropdownMenu
          aria-label={t("federation.switcher.menu")}
          bottomContent={
            <>
              {entries.length === 1 && !listError && (
                <p className="px-2 py-1 text-xs text-default-400">
                  {t("federation.switcher.empty")}
                </p>
              )}
              {listError && (
                <p className="px-2 py-1 text-xs text-danger" role="alert">
                  {listError}
                </p>
              )}
              {error && (
                <p className="px-2 py-1 text-xs text-danger" role="alert">
                  {error}
                </p>
              )}
            </>
          }
          className="w-64 font-sans text-left"
          disabledKeys={busy ? [...entries.map((entry) => entry.id), "retry", "manage"] : []}
          itemClasses={{ base: "px-2 py-2", title: "w-full min-w-0" }}
          topContent={
            <div className="px-2 pt-1.5 pb-1 text-xs text-default-500">
              {collapsed && <p className="truncate pb-1 font-medium">{label}</p>}
              {t("federation.switcher.switchTo")}
            </div>
          }
          onAction={(key) => {
            if (key === "retry") onOpen();
            else if (key === "manage") void manageDevices();
            else {
              const entry = entries.find((item) => item.id === key);

              if (entry) void select(entry);
            }
          }}
        >
          {entries.map((entry) => (
            <DropdownItem
              key={entry.id}
              aria-current={entry.isCurrent ? "true" : undefined}
              textValue={entry.name}
            >
              <span className="grid w-full grid-cols-[1rem_minmax(0,1fr)_auto_1rem] items-center gap-2 text-left">
                <span aria-hidden className="flex h-4 w-4 items-center justify-center">
                  {entry.isLocal ? (
                    <AiOutlineDesktop className="h-4 w-4" />
                  ) : (
                    <span
                      className={`h-2 w-2 rounded-full ${stateDotClass[dotState(entry.state)]}`}
                      data-state={ManagedServerStateLabel[dotState(entry.state)]}
                      data-testid="server-state-dot"
                    />
                  )}
                </span>
                <span className="min-w-0 truncate">{entry.name}</span>
                <span className="text-xs text-default-400">
                  {busyId === entry.id
                    ? t("federation.switcher.opening")
                    : entry.isLocal
                      ? t("federation.switcher.controller")
                      : entry.state !== undefined && labelledStates.has(entry.state)
                        ? t(`federation.servers.state.${entry.state}`)
                        : null}
                </span>
                <span aria-hidden className="flex h-4 w-4 items-center justify-center">
                  {entry.isCurrent && <AiOutlineCheck className="h-4 w-4 text-primary" />}
                </span>
              </span>
            </DropdownItem>
          ))}
          {listError ? (
            <DropdownItem key="retry" className="text-primary">
              {t("federation.retry")}
            </DropdownItem>
          ) : null}
          <DropdownItem
            key="manage"
            className="mt-1 border-t border-default-200 rounded-t-none"
            textValue={t("federation.switcher.manageDevices")}
          >
            <span className="grid grid-cols-[1rem_minmax(0,1fr)] items-center gap-2">
              <AiOutlineSetting aria-hidden className="h-4 w-4" />
              <span className="truncate">{t("federation.switcher.manageDevices")}</span>
            </span>
          </DropdownItem>
        </DropdownMenu>
      </Dropdown>
      {onPathMapping ? (
        <Dropdown
          disableAnimation
          closeOnSelect={false}
          isOpen={settingsOpen}
          placement={collapsed ? "right-start" : "bottom-end"}
          onOpenChange={setSettingsOpen}
        >
          <DropdownTrigger>{settingsTrigger}</DropdownTrigger>
          <DropdownMenu
            aria-label={t("federation.switcher.controllerSettings")}
            bottomContent={
              settingsError && (
                <p className="px-2 py-1 text-xs text-danger" role="alert">
                  {settingsError}
                </p>
              )
            }
            className="w-64 font-sans text-left"
            disabledKeys={settingsBusy ? ["mapping", "configuration"] : []}
            topContent={
              <p className="px-2 pt-1.5 pb-2 text-xs text-default-500">{controllerLabel}</p>
            }
            onAction={(key) => void openSettings(key === "mapping")}
          >
            <DropdownItem
              key="mapping"
              description={t("federation.switcher.pathMappingDescription", { name: currentName })}
            >
              {t("menu.client.pathMapping")}
            </DropdownItem>
            <DropdownItem key="configuration">
              {t("federation.switcher.openControllerConfiguration")}
            </DropdownItem>
          </DropdownMenu>
        </Dropdown>
      ) : (
        settingsTrigger
      )}
      {!onPathMapping && settingsError && (
        <p className="text-xs text-danger" role="alert">
          {settingsError}
        </p>
      )}
    </div>
  );
};

ServerSwitcher.displayName = "ServerSwitcher";

export default ServerSwitcher;
