"use client";

import type {
  BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
} from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOpenOutlined, WarningOutlined } from "@ant-design/icons";

import IdentityRecoveryLink from "./IdentityRecoveryLink";
import AppVersionPanel, { CurrentVersionValue } from "./AppVersionPanel";

import { Divider, Snippet } from "@/components/bakaui";
import { DataPathSource } from "@/sdk/constants";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
import { useIsPureClient, useIsRemoteClient } from "@/stores/remoteAccess";
import { useAppOptionsStore } from "@/stores/options";
import { Button, Chip } from "@/components/bakaui";
import FilePathValue from "@/components/FilePathValue";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import BApi from "@/sdk/BApi";
import { useRemoteServerUpdateRestart, useUpdateRestart } from "@/components/UpdateRestart";
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
  const [newVersion, setNewVersion] =
    useState<BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo>();
  const appUpdaterState = useAppUpdaterStateStore((state) => state);
  const appOptions = useAppOptionsStore((state) => state.data);
  // Every value in this section is forwarded, so in the desktop app showing a server it
  // manages it describes that server — including the update button, which updates that
  // machine. Saying whose information this is costs a word and prevents the mistake.
  const isPureClient = useIsPureClient();
  const isRemoteClient = useIsRemoteClient();
  const { restarting, restart } = useUpdateRestart();
  const remoteRestart = useRemoteServerUpdateRestart(appUpdaterState.status);

  const [checking, setChecking] = useState(true);
  const [checkError, setCheckError] = useState<string>();
  const checkRequestRef = useRef(0);
  const checkingRef = useRef(false);

  const checkNewAppVersion = useCallback(async (supersede = false) => {
    // Normal checks cannot be started twice; a saved channel change must be able
    // to replace an in-flight check for the previous channel.
    if (checkingRef.current && !supersede) return;

    const request = ++checkRequestRef.current;

    checkingRef.current = true;
    setChecking(true);
    setCheckError(undefined);
    try {
      const response = await BApi.updater.getNewAppVersion();

      if (request !== checkRequestRef.current) return;

      if (response.code || !response.data) {
        setCheckError(response.message || "configuration.appInfo.failedToGetLatestVersion");
      } else {
        setNewVersion(response.data);
      }
    } catch (error) {
      if (request !== checkRequestRef.current) return;

      setCheckError(
        error instanceof Error && error.message
          ? error.message
          : "configuration.appInfo.failedToGetLatestVersion",
      );
    } finally {
      if (request === checkRequestRef.current) {
        checkingRef.current = false;
        setChecking(false);
      }
    }
  }, []);

  useEffect(() => {
    void checkNewAppVersion();

    return () => {
      ++checkRequestRef.current;
      checkingRef.current = false;
    };
  }, [checkNewAppVersion]);

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
              {appInfo.appDataPath && !appInfo.dataInSystemPath && (
                <>
                  <Divider className="mx-1" orientation="vertical" />
                  <RelocationButton currentDataPath={appInfo.appDataPath} />
                </>
              )}
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
            {appInfo.dataInSystemPath && appInfo.appDataPath && (
              <div className="flex items-center gap-2 flex-wrap text-warning-500">
                <WarningOutlined className="text-sm" />
                <span className="text-xs">
                  {t("configuration.appInfo.tip.appDataPath.systemPathRiskNotice")}
                </span>
                <RelocationButton currentDataPath={appInfo.appDataPath} />
              </div>
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
        keywords: ["version", "build", "core", "current", "版本", "核心", "当前"],
        value: <CurrentVersionValue newVersion={newVersion} version={appInfo.coreVersion} />,
      },
      {
        id: "latestVersion",
        label: "configuration.appInfo.latestVersion",
        keywords: [
          "update",
          "upgrade",
          "release",
          "latest",
          "beta",
          "channel",
          "更新",
          "版本",
          "最新",
          "测试",
          "渠道",
        ],
        value: (
          <AppVersionPanel
            checkError={checkError}
            checking={checking}
            enablePreReleaseChannel={appOptions.enablePreReleaseChannel}
            newVersion={newVersion}
            percentage={appUpdaterState.percentage}
            remoteRestarting={isRemoteClient && remoteRestart.restarting}
            restartUnconfirmed={isRemoteClient && remoteRestart.timedOut}
            restarting={isRemoteClient ? remoteRestart.restarting : restarting}
            status={appUpdaterState.status}
            updateError={appUpdaterState.error}
            onChannelChange={(checked) => {
              applyPatches(
                BApi.options.patchAppOptions,
                { enablePreReleaseChannel: checked },
                () => void checkNewAppVersion(true),
              );
            }}
            onCheck={() => void checkNewAppVersion()}
            onDownload={() => BApi.updater.startUpdatingApp()}
            onRestart={() => {
              if (isRemoteClient) {
                remoteRestart.restart();
              } else {
                restart();
              }
            }}
          />
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
