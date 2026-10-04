import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";

import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  CheckCircleOutlined,
  DownloadOutlined,
  HistoryOutlined,
  InfoCircleOutlined,
  PoweroffOutlined,
  SyncOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { Button, Popover, Progress, Spinner, Switch, Tooltip } from "@/components/bakaui";
import { ChangelogButton } from "@/components/Changelog";
import ExternalLink from "@/components/ExternalLink";
import { UpdaterStatus } from "@/sdk/constants";

type VersionInfo = BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;

export const CurrentVersionValue = ({
  version,
  newVersion,
}: {
  version?: string;
  newVersion?: VersionInfo;
}) => {
  const { t } = useTranslation();
  const { runningVersion, installedVersion } = newVersion ?? {};
  const mismatch = runningVersion && installedVersion && runningVersion !== installedVersion;

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <span className="font-mono text-sm font-medium break-all">{version}</span>
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

  return (
    <div className="flex min-w-0 flex-col gap-3 py-1">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div
          aria-live="polite"
          className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2 text-sm"
        >
          {newVersion?.version && (
            <span className="font-mono font-medium break-all">{newVersion.version}</span>
          )}
          {renderStatus()}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {available && (
            <Button
              color="primary"
              size="sm"
              startContent={<DownloadOutlined />}
              variant="solid"
              onPress={onDownload}
            >
              {t("configuration.appInfo.clickToAutoUpdate")}
            </Button>
          )}
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
            <Button color="primary" size="sm" variant="solid" onPress={onDownload}>
              {t("configuration.appInfo.clickToRetry")}
            </Button>
          )}
          {newVersion?.version && (
            <ChangelogButton from={updateFrom} version={newVersion.version} />
          )}
          <Button
            aria-label={t<string>("configuration.appInfo.checkForUpdates")}
            color={checkFailed ? "primary" : "default"}
            isDisabled={checking}
            isLoading={checking}
            size="sm"
            startContent={!checking && <SyncOutlined />}
            variant={checkFailed ? "solid" : "bordered"}
            onPress={onCheck}
          >
            {t(
              checking
                ? "configuration.appInfo.checking"
                : checkFailed
                  ? "configuration.appInfo.clickToRetry"
                  : "configuration.appInfo.checkForUpdates",
            )}
          </Button>
        </div>
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
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-default-200/60 pt-2">
        <Button
          color="default"
          size="sm"
          startContent={<HistoryOutlined />}
          variant="light"
          onPress={() => navigate("/changelog")}
        >
          {t("configuration.appInfo.viewAllChangelogs")}
        </Button>
        {newVersion?.installers?.length > 0 && (
          <Popover
            trigger={
              <Button color="default" size="sm" startContent={<DownloadOutlined />} variant="light">
                {t("configuration.appInfo.autoUpdateFails")}
              </Button>
            }
          >
            <div className="flex max-w-xs flex-col gap-2 p-2">
              {newVersion.installers.map((installer) => (
                <ExternalLink key={installer.url} href={installer.url}>
                  {installer.name}
                </ExternalLink>
              ))}
            </div>
          </Popover>
        )}
        <Tooltip
          className="max-w-[300px]"
          color="secondary"
          content={t("configuration.others.enablePreRelease.tip")}
          placement="top"
        >
          <Switch
            aria-label={t<string>("configuration.appInfo.preReleaseChannel")}
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
    </div>
  );
};

export default AppVersionPanel;
