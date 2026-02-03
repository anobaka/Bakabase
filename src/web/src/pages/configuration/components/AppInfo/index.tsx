"use client";

import type {
  BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo,
  BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo,
} from "@/sdk/Api";

import Markdown from "react-markdown";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOpenOutlined } from "@ant-design/icons";

import { Popover, Divider, Icon, Progress, Snippet } from "@/components/bakaui";
import { UpdaterStatus } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import { bytesToSize } from "@/components/utils";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";
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
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface AppInfoProps {
  appInfo: Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>;
}

const AppInfo: React.FC<AppInfoProps> = ({ appInfo }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [newVersion, setNewVersion] = useState<BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo>();
  const appUpdaterState = useAppUpdaterStateStore((state) => state);

  const checkNewAppVersion = () => {
    BApi.updater.getNewAppVersion().then((a) => {
      setNewVersion(a.data);
    });
  };

  useEffect(() => {
    checkNewAppVersion();
    return () => {};
  }, []);

  const renderNewVersion = () => {
    switch (appUpdaterState.status) {
      case UpdaterStatus.UpToDate:
        return (
          <Chip radius="sm" variant="light" color="default">
            {t("configuration.appInfo.upToDate")}
          </Chip>
        );
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
            return (
              <Chip radius="sm" variant="light" color="default">
                {t("configuration.appInfo.upToDate")}
              </Chip>
            );
          }
        } else {
          return t("configuration.appInfo.failedToGetLatestVersion");
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

  const buildAppInfoDataSource = () => {
    const items: { label: string; value: React.ReactNode }[] = [
      {
        label: "configuration.appInfo.appDataPath",
        value: renderPathValue(
          appInfo.appDataPath,
          t("configuration.appInfo.tip.appDataPath"),
        ),
      },
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
        value: renderNewVersion(),
      },
    ];

    return items.map((x) => ({ ...x, label: t(x.label) }));
  };

  return (
    <div className="group">
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
