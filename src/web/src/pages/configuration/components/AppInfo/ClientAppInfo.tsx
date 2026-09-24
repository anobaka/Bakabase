"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";
import type {
  ClientAppInfo as ClientAppInfoModel,
  ClientStatus,
  ClientUpdaterState,
  ClientVersionInfo,
} from "@/core/clientApi";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { CheckCircleOutlined, FolderOpenOutlined, InfoCircleOutlined } from "@ant-design/icons";

import { clientApi } from "@/core/clientApi";
import { UpdaterStatus, RemoteDevicePlatform } from "@/sdk/constants";
import { Button, Chip, Divider, Progress, Snippet, Tooltip } from "@/components/bakaui";
import { ChangelogButton } from "@/components/Changelog";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import { useIsLegacyClient } from "@/stores/remoteAccess";

/** How often the client's update progress is re-read while it is downloading. */
const PROGRESS_INTERVAL = 1500;

/**
 * What this client is and whether it needs updating.
 *
 * The section below this one describes the server: in a thin client every value on it —
 * paths, core version, update state — is forwarded from the machine holding the library,
 * and pressing its update button updates that machine. The two are released separately
 * and are routinely different versions, so this is the only place the version people are
 * actually looking at appears.
 */
const ClientAppInfo: React.FC<{ query?: string }> = ({ query }) => {
  const { t } = useTranslation();
  // The retired thin client only. The desktop app's console answers as the same flavour
  // but has none of these routes: the app it belongs to is updated from its own window.
  const isPureClient = useIsLegacyClient();

  const [status, setStatus] = useState<ClientStatus>();
  const [paths, setPaths] = useState<ClientAppInfoModel>();
  const [updater, setUpdater] = useState<ClientUpdaterState>();
  const [newVersion, setNewVersion] = useState<ClientVersionInfo>();
  const timer = useRef<ReturnType<typeof setInterval>>();

  const readUpdaterState = useCallback(async () => {
    try {
      setUpdater(await clientApi.updater.state());
    } catch {
      // The client answering nothing about itself is not worth a toast; the section
      // simply shows what it last knew.
    }
  }, []);

  useEffect(() => {
    if (!isPureClient) {
      return;
    }

    clientApi
      .status()
      .then(setStatus)
      .catch(() => {});
    clientApi
      .appInfo()
      .then(setPaths)
      .catch(() => {});
    clientApi.updater
      .newVersion()
      .then(setNewVersion)
      .catch(() => {});
    readUpdaterState();
  }, [isPureClient, readUpdaterState]);

  // The server pushes its updater state over SignalR; the client has no such channel to
  // this window, so its progress is polled — but only while something is actually
  // downloading.
  useEffect(() => {
    if (updater?.status === UpdaterStatus.Running && !timer.current) {
      timer.current = setInterval(readUpdaterState, PROGRESS_INTERVAL);
    } else if (updater?.status !== UpdaterStatus.Running && timer.current) {
      clearInterval(timer.current);
      timer.current = undefined;
    }

    return () => {
      if (timer.current) {
        clearInterval(timer.current);
        timer.current = undefined;
      }
    };
  }, [updater?.status, readUpdaterState]);

  if (!isPureClient) {
    return null;
  }

  const startUpdating = async () => {
    setUpdater(await clientApi.updater.start());
    readUpdaterState();
  };

  const upToDate = (
    <span className="flex items-center gap-1 text-success">
      <CheckCircleOutlined className="text-base" />
      {t<string>("configuration.appInfo.upToDate")}
    </span>
  );

  const renderUpdate = () => {
    switch (updater?.status) {
      case UpdaterStatus.Running:
        return (
          <Progress
            showValueLabel
            className="w-[200px] pl-3"
            label={`${t("configuration.appInfo.downloading")} ${newVersion?.version ?? ""}`}
            size="sm"
            value={updater.percentage}
          />
        );
      case UpdaterStatus.PendingRestart:
        return (
          <Button color="primary" size="sm" onPress={() => clientApi.updater.restart()}>
            {t<string>("configuration.appInfo.restartToUpdate")}
          </Button>
        );
      case UpdaterStatus.Failed:
        return (
          <div className="flex items-center gap-2">
            <span className="text-danger">
              {t<string>("configuration.appInfo.failedToUpdateApp")}
            </span>
            <Button color="primary" size="sm" variant="light" onPress={startUpdating}>
              {t<string>("configuration.appInfo.clickToRetry")}
            </Button>
          </div>
        );
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
              {t<string>("configuration.appInfo.updateCheckUnavailable")}
            </span>
          </Tooltip>
        );
      default:
        break;
    }

    // A build that no installer put here cannot be updated, and saying "up to date" for
    // it claims a check that never happened.
    if (newVersion?.updateCheckUnavailable) {
      return (
        <Tooltip
          className="max-w-[360px]"
          color="secondary"
          content={t("configuration.appInfo.updateCheckUnavailable.tip")}
          placement="top"
        >
          <span className="flex items-center gap-1 text-foreground-500">
            <InfoCircleOutlined className="text-base" />
            {t<string>("configuration.appInfo.updateCheckUnavailable")}
          </span>
        </Tooltip>
      );
    }

    if (!newVersion?.version) {
      return upToDate;
    }

    return (
      <div className="flex items-center gap-2 flex-wrap">
        <Chip radius="sm" variant="light">
          {newVersion.version}
        </Chip>
        <Divider orientation="vertical" />
        {/* The same affordance the server's section uses, and the same notes: both
            products are cut from this repository at the same version numbers. The span
            is this client's own, from what it runs now to what it would install. */}
        <ChangelogButton from={status?.clientVersion} version={newVersion.version} />
        <Divider orientation="vertical" />
        <Button color="success" size="sm" variant="light" onPress={startUpdating}>
          {t<string>("configuration.appInfo.clickToAutoUpdate")}
        </Button>
      </div>
    );
  };

  const directoryRow = (
    id: "data" | "log" | "components",
    label: string,
    path: string | undefined,
    tip?: string,
  ): SettingItem[] =>
    path
      ? [
          {
            id: `client-${id}-path`,
            label,
            tip,
            keywords: ["path", "directory", "folder", "路径", "目录", "客户端"],
            render: () => (
              <div className="flex items-center gap-1 flex-wrap">
                <Snippet hideSymbol size="sm" variant="bordered">
                  {path}
                </Snippet>
                {/* Not the shared open-folder button: that one is a forwarded route
                    and would translate this path as though it were the server's. */}
                <Button
                  isIconOnly
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={() => clientApi.openDirectory(id)}
                >
                  <FolderOpenOutlined className="text-base" />
                </Button>
              </div>
            ),
          },
        ]
      : [];

  const items: SettingItem[] = [
    {
      id: "clientVersion",
      label: t("configuration.clientInfo.version"),
      keywords: ["version", "client", "版本", "客户端"],
      render: () => (
        <Chip radius="sm" variant="light">
          {status?.clientVersion ?? "-"}
        </Chip>
      ),
    },
    {
      id: "clientDevice",
      label: t("configuration.clientInfo.device"),
      tip: t("configuration.clientInfo.device.tip"),
      keywords: ["device", "name", "设备", "名称"],
      render: () => (
        <div className="flex items-center gap-2">
          <span>{status?.deviceName ?? "-"}</span>
          {status?.platform !== undefined && (
            <Chip radius="sm" size="sm" variant="flat">
              {RemoteDevicePlatform[status.platform]}
            </Chip>
          )}
        </div>
      ),
    },
    ...directoryRow(
      "data",
      t("configuration.clientInfo.dataDirectory"),
      paths?.dataDirectory,
      t("configuration.clientInfo.dataDirectory.tip"),
    ),
    ...directoryRow("log", t("configuration.clientInfo.logDirectory"), paths?.logDirectory),
    ...directoryRow(
      "components",
      t("configuration.clientInfo.componentsDirectory"),
      paths?.componentsDirectory,
      t("configuration.clientInfo.componentsDirectory.tip"),
    ),
    {
      id: "clientLatestVersion",
      label: t("configuration.clientInfo.latestVersion"),
      tip: t("configuration.clientInfo.latestVersion.tip"),
      keywords: ["update", "upgrade", "更新", "客户端"],
      render: renderUpdate,
    },
  ];

  return (
    <SettingsSection
      items={items}
      keywords={["client", "客户端", "version", "update"]}
      query={query}
      title={t("configuration.clientInfo.title")}
    />
  );
};

ClientAppInfo.displayName = "ClientAppInfo";

export default ClientAppInfo;
