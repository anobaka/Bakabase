import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";
import type { ReactNode } from "react";
import type { ComponentProps } from "react";

import { forwardRef } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  CheckCircleOutlined,
  DownloadOutlined,
  HistoryOutlined,
  InfoCircleOutlined,
  PoweroffOutlined,
  SyncOutlined,
  UploadOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { Button, Popover, Progress, Spinner, Switch, Tooltip } from "@/components/bakaui";
import { ChangelogButton } from "@/components/Changelog";
import ExternalLink from "@/components/ExternalLink";
import { UpdaterStatus } from "@/sdk/constants";

type VersionInfo = BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;

// Forward the popover's trigger props and ref to the same button that owns its
// tooltip, so both mouse and keyboard users can identify and open the menu.
const InstallerButton = forwardRef<HTMLButtonElement, ComponentProps<typeof Button>>(
  (props, ref) => {
    const { t } = useTranslation();
    const label = t<string>("configuration.appInfo.autoUpdateFails");

    return (
      <Tooltip content={label}>
        <Button ref={ref} isIconOnly aria-label={label} size="sm" variant="light" {...props}>
          <DownloadOutlined aria-hidden className="text-base" />
        </Button>
      </Tooltip>
    );
  },
);

InstallerButton.displayName = "InstallerButton";

const CurrentVersionValue = ({
  version,
  newVersion,
  children,
}: {
  version?: string;
  newVersion?: VersionInfo;
  children?: ReactNode;
}) => {
  const { t } = useTranslation();
  const { runningVersion, installedVersion } = newVersion ?? {};
  const mismatch = runningVersion && installedVersion && runningVersion !== installedVersion;

  return (
    <div className="flex flex-wrap items-center gap-1">
      <span className="font-mono text-sm font-medium break-all">{version}</span>
      {children}
      {version && <ChangelogButton isIconOnly version={version} />}
      {mismatch && (
        <Tooltip
          className="max-w-[360px]"
          color="warning"
          content={t("configuration.appInfo.runningVersionMismatch.tip", {
            running: runningVersion,
            installed: installedVersion,
          })}
          placement="top"
        >
          <span className="inline-flex items-center gap-1.5 text-warning-600 text-xs">
            <WarningOutlined aria-hidden />
            {t("configuration.appInfo.runningVersionMismatch", { installed: installedVersion })}
          </span>
        </Tooltip>
      )}
    </div>
  );
};

interface Props {
  version?: string;
  newVersion?: VersionInfo;
  status?: UpdaterStatus;
  percentage?: number;
  updateError?: string;
  checking: boolean;
  checkError?: string;
  enablePreReleaseChannel?: boolean;
  restarting: boolean;
  remoteRestarting: boolean;
  restartUnconfirmed: boolean;
  onCheck: () => void;
  onDownload: () => void;
  onRestart: () => void;
  onChannelChange: (checked: boolean) => void;
}

const AppVersionPanel = ({
  version,
  newVersion,
  status,
  percentage,
  updateError,
  checking,
  checkError,
  enablePreReleaseChannel,
  restarting,
  remoteRestarting,
  restartUnconfirmed,
  onCheck,
  onDownload,
  onRestart,
  onChannelChange,
}: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const operational = status === UpdaterStatus.Running || status === UpdaterStatus.PendingRestart;
  // A successful check can be more recent than the singleton's status. Keep an
  // active download/restart, but trust a concrete version or unavailability.
  // The backend also publishes Failed when a check fails, so a local check error
  // must take precedence over that status and retry checking, rather than downloading.
  const effectiveStatus =
    operational || status === UpdaterStatus.Failed
      ? status
      : newVersion?.updateCheckUnavailable
        ? UpdaterStatus.Unavailable
        : newVersion?.version && (status === undefined || status === UpdaterStatus.UpToDate)
          ? UpdaterStatus.Idle
          : (status ?? UpdaterStatus.Idle);
  const checkFailed = !operational && !checking && Boolean(checkError);
  const available =
    !checking && !checkFailed && effectiveStatus === UpdaterStatus.Idle && newVersion?.version;
  const updateFailed = !checking && !checkFailed && effectiveStatus === UpdaterStatus.Failed;
  const updateFrom = newVersion?.installedVersion ?? newVersion?.runningVersion;
  const installers = newVersion?.installers ?? [];
  const errorDetail =
    updateFailed && updateError
      ? updateError
      : !checking && checkError !== "configuration.appInfo.failedToGetLatestVersion"
        ? checkError
        : undefined;

  const renderStatus = () => {
    if (!operational && checking) {
      return (
        <span className="inline-flex items-center gap-2 text-foreground-500">
          <Spinner size="sm" />
          {t("configuration.appInfo.checking")}
        </span>
      );
    }

    if (checkFailed) {
      return (
        <span className="inline-flex items-center gap-1.5 text-danger">
          <WarningOutlined aria-hidden />
          {t("configuration.appInfo.failedToGetLatestVersion")}
        </span>
      );
    }

    switch (effectiveStatus) {
      case UpdaterStatus.Running:
        return <span className="text-primary">{t("configuration.appInfo.downloading")}</span>;
      case UpdaterStatus.PendingRestart:
        return (
          <span className="inline-flex items-center gap-1.5 text-primary">
            <CheckCircleOutlined aria-hidden />
            {t(
              remoteRestarting
                ? "appUpdate.serverRestarting"
                : "configuration.appInfo.readyToRestart",
            )}
          </span>
        );
      case UpdaterStatus.Failed:
        return (
          <span className="inline-flex items-center gap-1.5 text-danger">
            <WarningOutlined aria-hidden />
            {t("configuration.appInfo.failedToUpdateApp")}
          </span>
        );
      case UpdaterStatus.Unavailable:
        return (
          <Tooltip
            className="max-w-[360px]"
            color="secondary"
            content={t("configuration.appInfo.updateCheckUnavailable.tip")}
            placement="top"
          >
            <span className="inline-flex items-center gap-1.5 text-foreground-500">
              <InfoCircleOutlined aria-hidden />
              {t("configuration.appInfo.updateCheckUnavailable")}
            </span>
          </Tooltip>
        );
      default:
        if (available) {
          return <span className="text-primary">{t("configuration.appInfo.updateAvailable")}</span>;
        }

        if (newVersion?.channelBehindInstalled) {
          return (
            <Tooltip
              className="max-w-[360px]"
              color="warning"
              content={t("configuration.appInfo.channelBehind.tip")}
              placement="top"
            >
              <span className="inline-flex items-start gap-1.5 text-warning-600">
                <WarningOutlined aria-hidden className="mt-0.5 shrink-0" />
                {t("configuration.appInfo.channelBehind", {
                  channel: newVersion.channel,
                  latest: newVersion.channelLatestVersion,
                  installed: newVersion.installedVersion,
                })}
              </span>
            </Tooltip>
          );
        }

        return (
          <span className="inline-flex items-center gap-1.5 text-success">
            <CheckCircleOutlined aria-hidden />
            {t("configuration.appInfo.upToDate")}
          </span>
        );
    }
  };

  const checkLabel = t<string>(
    checking
      ? "configuration.appInfo.checking"
      : checkFailed
        ? "configuration.appInfo.clickToRetry"
        : "configuration.appInfo.checkForUpdates",
  );
  const updateLabel = t<string>("configuration.appInfo.upgradeToVersion", {
    version: newVersion?.version,
  });

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
        <CurrentVersionValue newVersion={newVersion} version={version}>
          {available && (
            <Tooltip content={updateLabel}>
              <Button
                isIconOnly
                aria-label={updateLabel}
                color="primary"
                size="sm"
                variant="flat"
                onPress={onDownload}
              >
                <UploadOutlined aria-hidden className="text-base" />
              </Button>
            </Tooltip>
          )}
        </CurrentVersionValue>
        <div aria-live="polite" className="flex min-w-0 flex-wrap items-center gap-1.5">
          {renderStatus()}
          {newVersion?.version && (
            <span className="font-mono font-medium break-all">{newVersion.version}</span>
          )}
        </div>
        {effectiveStatus === UpdaterStatus.PendingRestart && (
          <Button
            color="primary"
            isDisabled={restarting}
            isLoading={restarting}
            size="sm"
            startContent={!restarting && <PoweroffOutlined />}
            variant="solid"
            onPress={onRestart}
          >
            {t("configuration.appInfo.restartToUpdate")}
          </Button>
        )}
        {updateFailed && (
          <Button
            color="primary"
            size="sm"
            startContent={<SyncOutlined />}
            variant="flat"
            onPress={onDownload}
          >
            {t("configuration.appInfo.clickToRetry")}
          </Button>
        )}
        {newVersion?.version && (
          <ChangelogButton isIconOnly from={updateFrom} version={newVersion.version} />
        )}
        <Tooltip content={checkLabel}>
          <Button
            isIconOnly
            aria-label={t<string>("configuration.appInfo.checkForUpdates")}
            color={checkFailed ? "primary" : "default"}
            isDisabled={checking}
            isLoading={checking}
            size="sm"
            variant={checkFailed ? "flat" : "light"}
            onPress={onCheck}
          >
            {!checking && <SyncOutlined aria-hidden className="text-base" />}
          </Button>
        </Tooltip>
        <Tooltip content={t<string>("configuration.appInfo.viewAllChangelogs")}>
          <Button
            isIconOnly
            aria-label={t<string>("configuration.appInfo.viewAllChangelogs")}
            color="default"
            size="sm"
            variant="light"
            onPress={() => navigate("/changelog")}
          >
            <HistoryOutlined aria-hidden className="text-base" />
          </Button>
        </Tooltip>
        {installers.length > 0 && (
          <Popover trigger={<InstallerButton />}>
            <div className="flex max-w-xs flex-col gap-2 p-2">
              {installers.map((installer) => (
                <ExternalLink key={installer.url} href={installer.url}>
                  {installer.name}
                </ExternalLink>
              ))}
            </div>
          </Popover>
        )}
      </div>
      {effectiveStatus === UpdaterStatus.Running && (
        <Progress
          showValueLabel
          aria-label={t<string>("configuration.appInfo.downloading")}
          className="w-full max-w-md"
          isIndeterminate={percentage === undefined}
          size="sm"
          value={percentage}
        />
      )}
      {errorDetail && (
        <p className="text-xs text-danger break-words" role="alert">
          {t(errorDetail)}
        </p>
      )}
      {restartUnconfirmed && effectiveStatus === UpdaterStatus.PendingRestart && (
        <p className="text-xs text-warning-600">{t("appUpdate.serverRestartUnconfirmed")}</p>
      )}
      <Tooltip
        className="max-w-[300px]"
        color="secondary"
        content={t("configuration.others.enablePreRelease.tip")}
        placement="top"
      >
        <Switch
          aria-label={t<string>("configuration.appInfo.preReleaseChannel")}
          className="self-start"
          isSelected={enablePreReleaseChannel}
          size="sm"
          onValueChange={onChannelChange}
        >
          <span className="text-xs text-foreground-500">
            {t("configuration.appInfo.preReleaseChannel")}
          </span>
        </Switch>
      </Tooltip>
    </div>
  );
};

export default AppVersionPanel;
