"use client";

import type {
  BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
} from "@/sdk/Api";

import Markdown from "react-markdown";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { CheckCircleOutlined, FolderOpenOutlined } from "@ant-design/icons";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { Popover, Divider, Icon, Progress, Snippet, Tooltip } from "@/components/bakaui";
import { UpdaterStatus, DataPathSource } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import { bytesToSize } from "@/components/utils";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
import { useAppOptionsStore } from "@/stores/options";
import {
  Button,
  Table,
  TableRow,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  Chip,
  Modal,
  Switch,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { RelocationButton, RelocationRestartGate } from "@/pages/configuration/components/AppInfo/Relocation";
import { LegacyAppDataNoticeBanner } from "@/pages/configuration/components/AppInfo/LegacyNotice";

interface AppInfoProps {
  appInfo: Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>;
  applyPatches: <T>(api: (patches: T) => Promise<{ code?: number }>, patches: T, success?: (rsp: unknown) => void) => void;
}

const AppInfo: React.FC<AppInfoProps> = ({ appInfo, applyPatches }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [newVersion, setNewVersion] = useState<BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo>();
  const appUpdaterState = useAppUpdaterStateStore((state) => state);
  const appOptions = useAppOptionsStore((state) => state.data);

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

  const renderNewVersion = () => {
    // When the API has returned a concrete new version, trust it over a
    // possibly-stale UpToDate status carried by the backend singleton.
    const effectiveStatus = newVersion?.version
      && (appUpdaterState.status === undefined
        || appUpdaterState.status === UpdaterStatus.UpToDate)
      ? UpdaterStatus.Idle
      : appUpdaterState.status;
    switch (effectiveStatus) {
      case UpdaterStatus.UpToDate:
        return upToDateIndicator;
      case UpdaterStatus.Idle:
        if (newVersion) {
          if (newVersion.version) {
            return (
              <div className="flex items-center gap-2">
                <Chip radius="sm" variant="light">
                  {newVersion.version}
                </Chip>
                {newVersion.changelog && (
                  <>
                    <Divider orientation="vertical" />
                    <Button
                      color="secondary"
                      size="sm"
                      variant="light"
                      onPress={() => {
                        createPortal(Modal, {
                          size: "xl",
                          title: newVersion.version,
                          defaultVisible: true,
                          children: (
                            <Markdown
                              components={{
                                a: (props) => <ExternalLink {...props} target="_blank" />,
                              }}
                            >
                              {newVersion.changelog}
                            </Markdown>
                          ),
                          footer: { actions: ["cancel"] },
                        });
                      }}
                    >
                      {t("configuration.appInfo.changelog")}
                    </Button>
                  </>
                )}
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
                          <ExternalLink href={i.url}>
                            {i.name}({bytesToSize(i.size)})
                          </ExternalLink>
                        </div>
                      ))}
                    </Popover>
                  </>
                ) : undefined}
              </div>
            );
          } else {
            return upToDateIndicator;
          }
        } else {
          return upToDateIndicator;
        }
      case UpdaterStatus.Running:
        return (
          <Progress
            showValueLabel
            className="w-[200px] pl-3"
            size="sm"
            value={appUpdaterState.percentage}
            label={`${t("configuration.appInfo.downloading")} ${newVersion?.version ?? ""}`}
          />
        );
      case UpdaterStatus.PendingRestart:
        return (
          <Button
            size="sm"
            color="primary"
            onClick={() => {
              BApi.updater.restartAndUpdateApp();
            }}
          >
            {t("configuration.appInfo.restartToUpdate")}
          </Button>
        );
      case UpdaterStatus.Failed:
        return (
          <>
            {t("configuration.appInfo.failedToUpdateApp")}: {t(appUpdaterState.error!)}
            &nbsp;
            <Button
              variant="light"
              color="primary"
              onClick={() => {
                BApi.updater.startUpdatingApp();
              }}
            >
              {t("configuration.appInfo.clickToRetry")}
            </Button>
          </>
        );
      default:
        return <Icon type="loading" />;
    }
  };

  const renderPathValue = (path: string, description?: string) => (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1">
        <Snippet hideSymbol size="sm" variant="bordered">
          {path}
        </Snippet>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          color="primary"
          onPress={() => BApi.tool.openFileOrDirectory({ path })}
        >
          <FolderOpenOutlined className="text-base" />
        </Button>
      </div>
      {description && (
        <span className="text-xs text-foreground-400">{description}</span>
      )}
    </div>
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
      <Chip radius="sm" size="sm" variant="flat" color={color}>
        {label}
      </Chip>
    );
  };

  const buildAppInfoDataSource = () => {
    const items: { label: string; value: React.ReactNode }[] = [
      {
        label: "configuration.appInfo.appDataPath",
        value: (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-1 flex-wrap">
              <Snippet hideSymbol size="sm" variant="bordered">
                {appInfo.appDataPath}
              </Snippet>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                color="primary"
                onPress={() => BApi.tool.openFileOrDirectory({ path: appInfo.appDataPath })}
              >
                <FolderOpenOutlined className="text-base" />
              </Button>
              {renderDataPathSource()}
              <Divider orientation="vertical" className="mx-1" />
              {appInfo.appDataPath && (
                <RelocationButton currentDataPath={appInfo.appDataPath} />
              )}
            </div>
            <span className="text-xs text-foreground-400">
              {t("configuration.appInfo.tip.appDataPath")}
            </span>
            <span className="text-xs text-foreground-400">
              {t("configuration.appInfo.tip.appDataPath.manualMerge")}
            </span>
            {appInfo.dataPathSource === DataPathSource.Default && (
              <span className="text-xs text-warning-500">
                {t("configuration.appInfo.tip.appDataPath.uninstallNotice")}
              </span>
            )}
          </div>
        ),
      },
      ...((appInfo.anchorPath && appInfo.anchorPath !== appInfo.appDataPath)
        ? [{
          label: "configuration.appInfo.anchorPath",
          value: renderPathValue(appInfo.anchorPath),
        }]
        : []),
      {
        label: "configuration.appInfo.dataPath",
        value: renderPathValue(
          appInfo.dataPath,
          t("configuration.appInfo.tip.dataPath"),
        ),
      },
      {
        label: "configuration.appInfo.tempFilesPath",
        value: renderPathValue(
          appInfo.tempFilesPath,
          t("configuration.appInfo.tip.tempFilesPath"),
        ),
      },
      {
        label: "configuration.appInfo.logPath",
        value: renderPathValue(
          appInfo.logPath,
          t("configuration.appInfo.tip.logPath"),
        ),
      },
      {
        label: "configuration.appInfo.backupPath",
        value: renderPathValue(
          appInfo.backupPath,
          t("configuration.appInfo.tip.backupPath"),
        ),
      },
      {
        label: "configuration.appInfo.coreVersion",
        value: (
          <Chip radius="sm" variant="light">
            {appInfo.coreVersion}
          </Chip>
        ),
      },
      {
        label: "configuration.appInfo.latestVersion",
        value: (
          <div className="flex items-center gap-3 flex-wrap">
            {renderNewVersion()}
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

    return items.map((x) => ({ ...x, label: t(x.label) }));
  };

  return (
    <div className="group">
      <RelocationRestartGate />
      <LegacyAppDataNoticeBanner />
      <div className="settings">
        <Table isCompact removeWrapper>
          <TableHeader>
            <TableColumn width={200}>{t("configuration.appInfo.title")}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {buildAppInfoDataSource().map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className="hover:bg-[var(--bakaui-overlap-background)]"
                >
                  <TableCell>{c.label}</TableCell>
                  <TableCell>{c.value}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

AppInfo.displayName = "AppInfo";

export default AppInfo;
