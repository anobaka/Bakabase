"use client";

import type {
  BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
} from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import AppVersionPanel from "./AppVersionPanel";

import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
import { useIsPureClient, useIsRemoteClient } from "@/stores/remoteAccess";
import { useAppOptionsStore } from "@/stores/options";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import BApi from "@/sdk/BApi";
import { useRemoteServerUpdateRestart, useUpdateRestart } from "@/components/UpdateRestart";

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

  const buildAppInfoDataSource = (): SettingItem[] => {
    const items: (Omit<SettingItem, "label" | "render"> & {
      label: string;
      value: React.ReactNode;
    })[] = [
      {
        id: "coreVersion",
        label: "configuration.appInfo.coreVersion",
        keywords: [
          "version",
          "build",
          "core",
          "current",
          "核心",
          "当前",
          "update",
          "app updates",
          "upgrade",
          "release",
          "latest",
          "beta",
          "channel",
          "更新",
          "应用更新",
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
            version={appInfo.coreVersion}
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
      items={buildAppInfoDataSource()}
      keywords={["about", "app", "version", "关于", "应用"]}
      query={query}
      title={t(isPureClient ? "configuration.appInfo.title.server" : "configuration.appInfo.title")}
    />
  );
};

AppInfo.displayName = "AppInfo";

export default AppInfo;
