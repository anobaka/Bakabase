"use client";

import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  CloseOutlined,
  FileTextOutlined,
  ReloadOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { describeUpdateError } from "./describeUpdateError";

import { Button, Progress, Spinner, Tooltip } from "@/components/bakaui";
import { useChangelogModal } from "@/components/Changelog";
import BApi from "@/sdk/BApi";
import { UpdaterStatus } from "@/sdk/constants";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
import { clientApi } from "@/core/clientApi";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

export type AppUpdateBannerViewState =
  | { kind: "checking" }
  | { kind: "downloading"; version?: string; percentage?: number }
  | { kind: "pendingRestart"; version?: string }
  | { kind: "failed"; error?: string }
  | { kind: "hidden" };

interface ViewProps {
  collapsed: boolean;
  state: AppUpdateBannerViewState;
  onRestart: () => void;
  onRetry: () => void;
  onDismiss: () => void;
  /** Absent while no version is known yet — then no changelog button is offered. */
  onShowChangelog?: () => void;
}

// Mirrors HeroUI `Button size="sm" variant="bordered"` so the checking and
// downloading states sit at the same visual weight as the action buttons.
const buttonLikeBase =
  "min-h-8 px-3 py-1 rounded-lg border border-default-200 dark:border-default-100 bg-transparent text-xs text-foreground-500 flex items-center box-border";

export const AppUpdateBannerView: React.FC<ViewProps> = ({
  collapsed,
  state,
  onRestart,
  onRetry,
  onDismiss,
  onShowChangelog,
}) => {
  const { t } = useTranslation();

  if (state.kind === "hidden") return null;

  const wrapperClass = `flex flex-col gap-1.5 ${collapsed ? "px-2 py-1.5 items-center" : "px-3 py-1.5"}`;
  const labelClass = "text-xs text-foreground-500 truncate whitespace-nowrap";

  // One shape for every state: the primary action fills the row beside a single
  // trailing icon button. Collapsed, the pair stacks instead.
  const actionRow = (primary: React.ReactNode, trailing: React.ReactNode) => (
    <div className={`flex gap-1 ${collapsed ? "flex-col items-center" : "items-center"}`}>
      <div className={collapsed ? "" : "flex-1 min-w-0"}>{primary}</div>
      {trailing}
    </div>
  );

  // Nobody should be asked to install a version they cannot read about first, so
  // the primary action gives up the far edge of the rail to the release notes.
  const withChangelog = (primary: React.ReactNode) => {
    if (!onShowChangelog) return primary;

    const changelogLabel = t<string>("changelog.view");

    return actionRow(
      primary,
      <Tooltip content={changelogLabel} placement="right">
        <Button
          isIconOnly
          aria-label={changelogLabel}
          size="sm"
          variant="light"
          onPress={onShowChangelog}
        >
          <FileTextOutlined />
        </Button>
      </Tooltip>,
    );
  };

  if (state.kind === "checking") {
    return (
      <div className={wrapperClass}>
        <Tooltip
          content={t<string>("appUpdate.checking")}
          isDisabled={!collapsed}
          placement="right"
        >
          <div className={`${buttonLikeBase} gap-2 ${collapsed ? "px-2" : ""}`}>
            <Spinner size="sm" />
            {!collapsed && <span className={labelClass}>{t<string>("appUpdate.checking")}</span>}
          </div>
        </Tooltip>
      </div>
    );
  }

  if (state.kind === "pendingRestart") {
    return (
      <div className={wrapperClass}>
        {withChangelog(
          <Tooltip
            content={t<string>("appUpdate.restartToUpdate")}
            isDisabled={!collapsed}
            placement="right"
          >
            <Button
              color="primary"
              fullWidth={!collapsed}
              isIconOnly={collapsed}
              size="sm"
              variant="flat"
              onPress={onRestart}
            >
              <ReloadOutlined />
              {!collapsed && (
                <span className={labelClass}>{t<string>("appUpdate.restartToUpdate")}</span>
              )}
            </Button>
          </Tooltip>,
        )}
      </div>
    );
  }

  if (state.kind === "failed") {
    const described = describeUpdateError(state.error);
    // An expired certificate on the update endpoint reaches us as .NET's generic
    // "SSL connection could not be established", which reads like a flaky network.
    // Say what actually went wrong, and keep the raw text underneath it.
    const explanation = described ? t<string>(described.messageKey) : undefined;
    const tooltipContent = [t<string>("appUpdate.failed"), explanation, described?.detail]
      .filter(Boolean)
      .join(" — ");

    return (
      <div className={wrapperClass}>
        {/* The trailing slot is the dismiss button here rather than the changelog
            one: retry and dismiss already fill the row, and a third button would
            squeeze the label out. The row used to be `justify-between`, which
            left retry at its content width while the other states spanned the rail. */}
        {actionRow(
          <Tooltip className="max-w-[320px]" content={tooltipContent} placement="right">
            <Button
              aria-label={t<string>("appUpdate.clickToRetry")}
              color="danger"
              fullWidth={!collapsed}
              isIconOnly={collapsed}
              size="sm"
              variant="flat"
              onPress={onRetry}
            >
              <WarningOutlined />
              {!collapsed && (
                <span className={labelClass}>{t<string>("appUpdate.clickToRetry")}</span>
              )}
            </Button>
          </Tooltip>,
          <Tooltip content={t<string>("appUpdate.dismiss")} placement="right">
            <Button
              isIconOnly
              aria-label={t<string>("appUpdate.dismiss")}
              size="sm"
              variant="light"
              onPress={onDismiss}
            >
              <CloseOutlined />
            </Button>
          </Tooltip>,
        )}
        {/* Expanded sidebar previously showed nothing at all — the cause was only
            reachable by hovering while collapsed. */}
        {!collapsed && explanation && (
          <div className="text-[11px] leading-snug text-foreground-500 break-words">
            {explanation}
          </div>
        )}
      </div>
    );
  }

  const versionLabel = state.version ?? "";

  return (
    <div className={wrapperClass}>
      {withChangelog(
        <Tooltip
          content={`${t<string>("appUpdate.downloading")} ${versionLabel}`.trim()}
          isDisabled={!collapsed}
          placement="right"
        >
          <div
            className={`${buttonLikeBase} flex-col justify-center gap-1 w-full ${collapsed ? "px-2" : "px-2.5"}`}
          >
            {!collapsed && (
              <div className={labelClass}>
                {t<string>("appUpdate.downloading")} {versionLabel}
              </div>
            )}
            <Progress
              aria-label="downloading"
              className={collapsed ? "w-11" : "w-full"}
              isIndeterminate={state.percentage === undefined}
              size="sm"
              value={state.percentage}
            />
          </div>
        </Tooltip>,
      )}
    </div>
  );
};

interface Props {
  collapsed: boolean;
}

/**
 * The sidebar's update banner, which in a thin client is about the client.
 *
 * The two flavours ask different questions of different programs, so they are two
 * components rather than one with branches: the all-in-one's path below is exactly what
 * it has always been, down to the auto-start, and nothing about it is now conditional on
 * a store that only a client populates.
 */
const AppUpdateBanner: React.FC<Props> = ({ collapsed }) => {
  const isPureClient = useIsPureClient();
  const clientHost = useRemoteAccessStore((state) => state.clientHost);

  if (!isPureClient) {
    return <ServerUpdateBanner collapsed={collapsed} />;
  }

  // Nothing until the PureClient says which program it is. In the desktop app's console
  // neither banner is right: `BApi.updater` would update the managed server behind the
  // user's back, and the app's own updater lives in the app's own window, not here.
  return clientHost === "legacy" ? <ClientUpdateBanner collapsed={collapsed} /> : null;
};

const ServerUpdateBanner: React.FC<Props> = ({ collapsed }) => {
  const appUpdaterState = useAppUpdaterStateStore((s) => s);
  const showChangelog = useChangelogModal();

  const [checking, setChecking] = useState(true);
  const [newVersion, setNewVersion] = useState<
    BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo | undefined
  >();
  const autoStartedRef = useRef(false);

  useEffect(() => {
    BApi.updater
      .getNewAppVersion()
      .then((a) => setNewVersion(a.data))
      .finally(() => setChecking(false));
  }, []);

  // Auto-start download once a new version is known and we're not already busy.
  useEffect(() => {
    if (autoStartedRef.current) return;
    if (!newVersion?.version) return;

    const status = appUpdaterState.status;
    const busy =
      status === UpdaterStatus.Running ||
      status === UpdaterStatus.PendingRestart ||
      status === UpdaterStatus.Failed;

    if (busy) return;

    autoStartedRef.current = true;
    BApi.updater.startUpdatingApp();
  }, [newVersion, appUpdaterState.status]);

  const hasNewVersion = Boolean(newVersion?.version);
  const status = appUpdaterState.status;

  let viewState: AppUpdateBannerViewState;

  if (checking) {
    viewState = { kind: "checking" };
  } else if (status === UpdaterStatus.PendingRestart) {
    viewState = { kind: "pendingRestart", version: newVersion?.version };
  } else if (status === UpdaterStatus.Failed) {
    // Let the user dismiss the failed banner for the current run so a transient
    // network failure doesn't leave a persistent warning icon in the sidebar.
    viewState = appUpdaterState.failureDismissed
      ? { kind: "hidden" }
      : { kind: "failed", error: appUpdaterState.error };
  } else if (
    status === UpdaterStatus.Running ||
    (hasNewVersion && status !== UpdaterStatus.UpToDate)
  ) {
    viewState = {
      kind: "downloading",
      version: newVersion?.version,
      percentage: appUpdaterState.percentage,
    };
  } else {
    viewState = { kind: "hidden" };
  }

  return (
    <AppUpdateBannerView
      collapsed={collapsed}
      state={viewState}
      onDismiss={() => appUpdaterState.dismissFailure()}
      onRestart={() => BApi.updater.restartAndUpdateApp()}
      onRetry={() => BApi.updater.startUpdatingApp()}
      onShowChangelog={
        newVersion?.version
          ? () =>
              showChangelog(newVersion.version, {
                // Velopack decides updates against the install manifest, so
                // installedVersion is what this update actually moves the user off;
                // runningVersion only differs on a dev build, where it is still a
                // better lower bound than none.
                from: newVersion.installedVersion ?? newVersion.runningVersion,
              })
          : undefined
      }
    />
  );
};

/** While the client is downloading, how often its progress is re-read. */
const CLIENT_POLL_INTERVAL = 1500;

/**
 * The same banner, for the program the window belongs to.
 *
 * `BApi.updater.*` is forwarded, so in a thin client every call on it is about the server
 * — including the auto-start above, which would have this window quietly update someone
 * else's machine the moment it opened. The server's own new version is not silently
 * dropped: it is reported on the connection page, where the decision to update the
 * machine holding the library belongs.
 */
const ClientUpdateBanner: React.FC<Props> = ({ collapsed }) => {
  const [checking, setChecking] = useState(true);
  const [version, setVersion] = useState<string>();
  const [status, setStatus] = useState<UpdaterStatus>();
  const [percentage, setPercentage] = useState<number>();
  const [error, setError] = useState<string>();
  const [dismissed, setDismissed] = useState(false);
  const autoStartedRef = useRef(false);

  // The server pushes its updater state over SignalR. The client has no channel into this
  // window, so its progress is polled — and only while it is actually downloading.
  const readState = useCallback(async () => {
    try {
      const state = await clientApi.updater.state();

      setStatus(state.status);
      setPercentage(state.percentage);
      setError(state.error);
    } catch {
      // Nothing to say; the banner keeps showing what it last knew.
    }
  }, []);

  useEffect(() => {
    clientApi.updater
      .newVersion()
      .then((v) => setVersion(v?.version ?? undefined))
      .catch(() => {})
      .finally(() => setChecking(false));
    readState();
  }, [readState]);

  useEffect(() => {
    if (status !== UpdaterStatus.Running) {
      return;
    }

    const timer = setInterval(readState, CLIENT_POLL_INTERVAL);

    return () => clearInterval(timer);
  }, [status, readState]);

  useEffect(() => {
    if (autoStartedRef.current || !version) {
      return;
    }

    const busy =
      status === UpdaterStatus.Running ||
      status === UpdaterStatus.PendingRestart ||
      status === UpdaterStatus.Failed;

    if (busy) {
      return;
    }

    autoStartedRef.current = true;
    clientApi.updater
      .start()
      .then((state) => setStatus(state.status))
      .catch(() => {});
  }, [version, status]);

  let viewState: AppUpdateBannerViewState;

  if (checking) {
    viewState = { kind: "checking" };
  } else if (status === UpdaterStatus.PendingRestart) {
    viewState = { kind: "pendingRestart" };
  } else if (status === UpdaterStatus.Failed) {
    viewState = dismissed ? { kind: "hidden" } : { kind: "failed", error };
  } else if (status === UpdaterStatus.Running || (version && status !== UpdaterStatus.UpToDate)) {
    viewState = { kind: "downloading", version, percentage };
  } else {
    viewState = { kind: "hidden" };
  }

  return (
    <AppUpdateBannerView
      collapsed={collapsed}
      state={viewState}
      onDismiss={() => setDismissed(true)}
      onRestart={() => clientApi.updater.restart()}
      onRetry={() => {
        setDismissed(false);
        clientApi.updater.start().then((state) => setStatus(state.status));
      }}
    />
  );
};

export default AppUpdateBanner;
