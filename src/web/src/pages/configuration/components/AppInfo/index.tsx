"use client";

import type {
  BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
} from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  CheckCircleOutlined,
  FolderOpenOutlined,
  InfoCircleOutlined,
  WarningOutlined,
} from "@ant-design/icons";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import IdentityRecoveryLink from "./IdentityRecoveryLink";

import { Popover, Divider, Icon, Progress, Snippet, Tooltip } from "@/components/bakaui";
import { UpdaterStatus, DataPathSource } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
import { useIsPureClient } from "@/stores/remoteAccess";
import { useAppOptionsStore } from "@/stores/options";
import { Button, Chip, Switch } from "@/components/bakaui";
import { ChangelogButton } from "@/components/Changelog";
import FilePathValue from "@/components/FilePathValue";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import BApi from "@/sdk/BApi";
import {
  RelocationButton,
  RelocationRestartGate,
} from "@/pages/configuration/components/AppInfo/Relocation";
import { LegacyAppDataNoticeBanner } from "@/pages/configuration/components/AppInfo/LegacyNotice";

interface AppInfoProps {
  appInfo: Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>;
  applyPatches: <T>(
    api: (patches: T) => Promise<{ code?: number }>,
    patches: T,
    success?: (rsp: unknown) => void,
  ) => void;
  query?: string;
}

const AppInfo: React.FC<AppInfoProps> = ({ appInfo, applyPatches, query }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [newVersion, setNewVersion] =
    useState<BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo>();
  const appUpdaterState = useAppUpdaterStateStore((state) => state);
  const appOptions = useAppOptionsStore((state) => state.data);
  // Every value in this section is forwarded, so in the desktop app showing a server it
  // manages it describes that server — including the update button, which updates that
  // machine. Saying whose information this is costs a word and prevents the mistake.
  const isPureClient = useIsPureClient();

  // The version an update moves the user off, so the changelog can show the whole span
  // rather than only the newest release. Velopack decides against the install manifest;
  // runningVersion only differs on a dev build, where it is still better than no bound.
  const updateFrom = newVersion?.installedVersion ?? newVersion?.runningVersion;

  const checkNewAppVersion = () => {
    BApi.updater.getNewAppVersion().then((a) => {
      setNewVersion(a.data);
    });
  };

  useEffect(() => {
    checkNewAppVersion();

    return () => {};
  }, []);

  const upToDateIndicator = (
    <span className="flex items-center gap-1 text-success">
      <CheckCircleOutlined className="text-base" />
      {t("configuration.appInfo.upToDate")}
    </span>
  );

  // "No new version" is not always what it looks like: when the selected channel's newest
  // release is older than the installed one (a pre-release build with the pre-release
  // channel off, say), the check will answer that forever. Say so instead of showing a
  // green tick the user cannot argue with.
  const renderNoNewVersion = () => {
    if (!newVersion?.channelBehindInstalled) {
      return upToDateIndicator;
    }

    return (
      <Tooltip
        className="max-w-[360px]"
        color="warning"
        content={t("configuration.appInfo.channelBehind.tip")}
        placement="top"
      >
        <span className="flex items-center gap-1 text-warning">
          <WarningOutlined className="text-base" />
          {t("configuration.appInfo.channelBehind", {
            channel: newVersion.channel,
            latest: newVersion.channelLatestVersion,
            installed: newVersion.installedVersion,
          })}
        </span>
      </Tooltip>
    );
  };

  // Velopack compares the feed against the install manifest, never against the assembly
  // the app is running. Copy a build into an existing install directory — the ordinary
  // local dev loop — and the version on screen stops being the one updates are decided
  // for, which reads as "it keeps telling me I'm up to date".
  const renderRunningVersionMismatch = () => {
    const { runningVersion, installedVersion } = newVersion ?? {};

    if (!runningVersion || !installedVersion || runningVersion === installedVersion) {
      return null;
    }

    return (
      <Tooltip
        className="max-w-[360px]"
        color="warning"
        content={t("configuration.appInfo.runningVersionMismatch.tip", {
          running: runningVersion,
          installed: installedVersion,
        })}
        placement="top"
      >
        <span className="flex items-center gap-1 text-warning text-sm">
          <WarningOutlined className="text-base" />
          {t("configuration.appInfo.runningVersionMismatch", { installed: installedVersion })}
        </span>
      </Tooltip>
    );
  };

  const renderNewVersion = () => {
    // When the API has returned a concrete new version, trust it over a
    // possibly-stale UpToDate status carried by the backend singleton.
    const effectiveStatus =
      newVersion?.version &&
      (appUpdaterState.status === undefined || appUpdaterState.status === UpdaterStatus.UpToDate)
        ? UpdaterStatus.Idle
        : appUpdaterState.status;

    switch (effectiveStatus) {
      case UpdaterStatus.UpToDate:
        return renderNoNewVersion();
      case UpdaterStatus.Unavailable:
        return (
          <Tooltip
            className="max-w-[360px]"
            color="secondary"
            content={t("configuration.appInfo.updateCheckUnavailable.tip")}
            placement="top"
          >
            <span className="flex items-center gap-1 text-foreground-500">
              <InfoCircleOutlined className="text-base" />
              {t("configuration.appInfo.updateCheckUnavailable")}
            </span>
          </Tooltip>
        );
      case UpdaterStatus.Idle:
        if (newVersion) {
          if (newVersion.version) {
            return (
              <div className="flex items-center gap-2">
                <Chip radius="sm" variant="light">
                  {newVersion.version}
                </Chip>
                <Divider orientation="vertical" />
                <ChangelogButton from={updateFrom} version={newVersion.version} />
                <Divider orientation="vertical" />
                <Button
                  color="success"
                  size="sm"
                  variant="light"
                  onClick={() => {
                    BApi.updater.startUpdatingApp();
                  }}
                >
                  {t("configuration.appInfo.clickToAutoUpdate")}
                </Button>
                {newVersion.installers?.length > 0 ? (
                  <>
                    <Divider orientation="vertical" />
                    <Popover
                      trigger={
                        <Button color="primary" size="sm" variant="light">
                          {t("configuration.appInfo.autoUpdateFails")}
                        </Button>
                      }
                    >
                      {newVersion.installers.map((i) => (
                        <div key={i.url}>
                          <ExternalLink href={i.url}>{i.name}</ExternalLink>
                        </div>
                      ))}
                    </Popover>
                  </>
                ) : undefined}
              </div>
            );
          } else {
            return renderNoNewVersion();
          }
        } else {
          return renderNoNewVersion();
        }
      // Downloading, pending restart and failed all concern a known version, so
      // each keeps the changelog within reach — the notes are most wanted right
      // before the restart that applies them.
      case UpdaterStatus.Running:
        return (
          <div className="flex items-center gap-2">
            <Progress
              showValueLabel
              className="w-[200px] pl-3"
              label={`${t("configuration.appInfo.downloading")} ${newVersion?.version ?? ""}`}
              size="sm"
              value={appUpdaterState.percentage}
            />
            <ChangelogButton from={updateFrom} version={newVersion?.version} />
          </div>
        );
      case UpdaterStatus.PendingRestart:
        return (
          <div className="flex items-center gap-2">
            <Button
              color="primary"
              size="sm"
              onClick={() => {
                BApi.updater.restartAndUpdateApp();
              }}
            >
              {t("configuration.appInfo.restartToUpdate")}
            </Button>
            <ChangelogButton from={updateFrom} version={newVersion?.version} />
          </div>
        );
      case UpdaterStatus.Failed:
        return (
          <div className="flex items-center gap-2 flex-wrap">
            <span>
              {t("configuration.appInfo.failedToUpdateApp")}: {t(appUpdaterState.error!)}
            </span>
            <Button
              color="primary"
              variant="light"
              onClick={() => {
                BApi.updater.startUpdatingApp();
              }}
            >
              {t("configuration.appInfo.clickToRetry")}
            </Button>
            {newVersion?.version && (
              <ChangelogButton from={updateFrom} version={newVersion.version} />
            )}
          </div>
        );
      default:
        return <Icon type="loading" />;
    }
  };

  const renderPathValue = (path: string, description?: string) => (
    <FilePathValue description={description} path={path} />
  );

  const renderDataPathSource = () => {
    const source = appInfo.dataPathSource;
    const envVarName = appInfo.envVarName ?? "BAKABASE_DATA_DIR";

    let label: string;
    let color: "default" | "primary" | "warning" = "default";

    switch (source) {
      case DataPathSource.Environment:
        label = t("configuration.appInfo.dataPathSource.environment", { name: envVarName });
        color = "warning";
        break;
      case DataPathSource.UserConfigured:
        label = t("configuration.appInfo.dataPathSource.userConfigured");
        color = "primary";
        break;
      case DataPathSource.Default:
      default:
        label = t("configuration.appInfo.dataPathSource.default");
        color = "default";
        break;
    }

    return (
      <Chip color={color} radius="sm" size="sm" variant="flat">
        {label}
      </Chip>
    );
  };

  const buildAppInfoDataSource = (): SettingItem[] => {
    const items: (Omit<SettingItem, "label" | "render"> & {
      label: string;
      value: React.ReactNode;
    })[] = [
      {
        id: "appDataPath",
        label: "configuration.appInfo.appDataPath",
        keywords: ["path", "directory", "folder", "数据", "目录"],
        value: (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-1 flex-wrap">
              <Snippet hideSymbol size="sm" variant="bordered">
                {appInfo.appDataPath}
              </Snippet>
              <Button
                isIconOnly
                color="primary"
                size="sm"
                variant="light"
                onPress={() => BApi.tool.openFileOrDirectory({ path: appInfo.appDataPath })}
              >
                <FolderOpenOutlined className="text-base" />
              </Button>
              {renderDataPathSource()}
              <Divider className="mx-1" orientation="vertical" />
              {appInfo.appDataPath && <RelocationButton currentDataPath={appInfo.appDataPath} />}
            </div>
            <span className="text-xs text-foreground-400">
              {t("configuration.appInfo.tip.appDataPath")}
            </span>
            <span className="text-xs text-foreground-400">
              {t("configuration.appInfo.tip.appDataPath.manualMerge")}
            </span>
            <IdentityRecoveryLink />
            {appInfo.dataInInstallRoot && (
              <span className="text-xs text-warning-500">
                {t("configuration.appInfo.tip.appDataPath.installRootRiskNotice")}
              </span>
            )}
          </div>
        ),
      },
      ...(appInfo.anchorPath && appInfo.anchorPath !== appInfo.appDataPath
        ? [
            {
              id: "anchorPath",
              label: "configuration.appInfo.anchorPath",
              keywords: ["path", "目录"],
              value: renderPathValue(appInfo.anchorPath, t("configuration.appInfo.tip.anchorPath")),
            },
          ]
        : []),
      {
        id: "dataPath",
        label: "configuration.appInfo.dataPath",
        keywords: ["path", "database", "目录", "数据"],
        value: renderPathValue(appInfo.dataPath, t("configuration.appInfo.tip.dataPath")),
      },
      {
        id: "tempFilesPath",
        label: "configuration.appInfo.tempFilesPath",
        keywords: ["path", "cache", "temp", "缓存", "临时"],
        value: renderPathValue(appInfo.tempFilesPath, t("configuration.appInfo.tip.tempFilesPath")),
      },
      {
        id: "logPath",
        label: "configuration.appInfo.logPath",
        keywords: ["path", "log", "日志", "目录"],
        value: renderPathValue(appInfo.logPath, t("configuration.appInfo.tip.logPath")),
      },
      {
        id: "backupPath",
        label: "configuration.appInfo.backupPath",
        keywords: ["path", "backup", "备份"],
        value: (
          <div className="flex flex-col gap-1">
            {renderPathValue(appInfo.backupPath, t("configuration.appInfo.tip.backupPath"))}
            <IdentityRecoveryLink />
          </div>
        ),
      },
      {
        id: "coreVersion",
        label: "configuration.appInfo.coreVersion",
        keywords: ["version", "build", "版本"],
        value: (
          <div className="flex items-center gap-2 flex-wrap">
            <Chip radius="sm" variant="light">
              {appInfo.coreVersion}
            </Chip>
            {appInfo.coreVersion && <ChangelogButton version={appInfo.coreVersion} />}
            {renderRunningVersionMismatch()}
          </div>
        ),
      },
      {
        id: "latestVersion",
        label: "configuration.appInfo.latestVersion",
        keywords: ["update", "upgrade", "release", "更新", "版本"],
        value: (
          <div className="flex items-center gap-3 flex-wrap">
            {renderNewVersion()}
            <Divider orientation="vertical" />
            <Button
              color="primary"
              size="sm"
              variant="light"
              onPress={() => navigate("/changelog")}
            >
              {t("configuration.appInfo.viewAllChangelogs")}
            </Button>
            <Divider orientation="vertical" />
            <div className="flex items-center gap-1">
              <Tooltip
                className="max-w-[300px]"
                color="secondary"
                content={t("configuration.others.enablePreRelease.tip")}
                placement="top"
              >
                <div className="flex items-center gap-1 text-foreground-500">
                  <span className="text-sm">{t("configuration.others.enablePreRelease")}</span>
                  <AiOutlineQuestionCircle className="text-base" />
                </div>
              </Tooltip>
              <Switch
                isSelected={appOptions.enablePreReleaseChannel}
                size="sm"
                onValueChange={(checked) => {
                  applyPatches(
                    BApi.options.patchAppOptions,
                    { enablePreReleaseChannel: checked },
                    () => checkNewAppVersion(),
                  );
                }}
              />
            </div>
          </div>
        ),
      },
    ];

    return items.map(({ value, ...x }) => ({ ...x, label: t(x.label), render: () => value }));
  };

  return (
    <SettingsSection
      header={
        <>
          <RelocationRestartGate />
          <LegacyAppDataNoticeBanner />
        </>
      }
      items={buildAppInfoDataSource()}
      keywords={["about", "app", "version", "关于", "应用"]}
      query={query}
      title={t(isPureClient ? "configuration.appInfo.title.server" : "configuration.appInfo.title")}
    />
  );
};

AppInfo.displayName = "AppInfo";

export default AppInfo;
