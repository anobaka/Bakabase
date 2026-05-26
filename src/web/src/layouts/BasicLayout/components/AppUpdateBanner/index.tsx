"use client";

import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ReloadOutlined, WarningOutlined } from "@ant-design/icons";

import { Button, Progress, Spinner, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { UpdaterStatus } from "@/sdk/constants";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";

export type AppUpdateBannerViewState =
  | { kind: "checking" }
  | { kind: "downloading"; version?: string; percentage?: number }
  | { kind: "pendingRestart" }
  | { kind: "failed"; error?: string }
  | { kind: "hidden" };

interface ViewProps {
  collapsed: boolean;
  state: AppUpdateBannerViewState;
  onRestart: () => void;
  onRetry: () => void;
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
}) => {
  const { t } = useTranslation();

  if (state.kind === "hidden") return null;

  const wrapperClass = `flex flex-col gap-1.5 ${collapsed ? "px-2 py-1.5 items-center" : "px-3 py-1.5"}`;
  const labelClass = "text-xs text-foreground-500 truncate whitespace-nowrap";

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
        <Tooltip
          content={t<string>("appUpdate.restartToUpdate")}
          isDisabled={!collapsed}
          placement="right"
        >
          <Button
            color="primary"
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
        </Tooltip>
      </div>
    );
  }

  if (state.kind === "failed") {
    return (
      <div className={wrapperClass}>
        <Tooltip
          content={`${t<string>("appUpdate.failed")} ${state.error ? `(${t(state.error)})` : ""}`}
          isDisabled={!collapsed}
          placement="right"
        >
          <Button color="danger" isIconOnly={collapsed} size="sm" variant="flat" onPress={onRetry}>
            <WarningOutlined />
            {!collapsed && (
              <span className={labelClass}>{t<string>("appUpdate.clickToRetry")}</span>
            )}
          </Button>
        </Tooltip>
      </div>
    );
  }

  const versionLabel = state.version ?? "";
  return (
    <div className={wrapperClass}>
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
      </Tooltip>
    </div>
  );
};

interface Props {
  collapsed: boolean;
}

const AppUpdateBanner: React.FC<Props> = ({ collapsed }) => {
  const appUpdaterState = useAppUpdaterStateStore((s) => s);

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
    viewState = { kind: "pendingRestart" };
  } else if (status === UpdaterStatus.Failed) {
    viewState = { kind: "failed", error: appUpdaterState.error };
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
      onRestart={() => BApi.updater.restartAndUpdateApp()}
      onRetry={() => BApi.updater.startUpdatingApp()}
    />
  );
};

export default AppUpdateBanner;
