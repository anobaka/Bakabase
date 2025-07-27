"use client";

import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { Popover, Divider, Icon, Progress } from "@/components/bakaui";
import { UpdaterStatus } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import { bytesToSize } from "@/components/utils";
import { useAppUpdaterStateStore } from "@/models/appUpdaterState";
import { useDependentComponentContextsStore } from "@/models/dependentComponentContexts";
import DependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import {
  Button,
  Table,
  TableRow,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  Tooltip,
  Chip,
  Modal,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Version =
  components["schemas"]["Bakabase.Infrastructures.Components.App.Upgrade.Abstractions.AppVersionInfo"];

export default ({ appInfo }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [newVersion, setNewVersion] = useState<Version>();
  const appUpdaterState = useAppUpdaterStateStore((state) => state);
  // const appUpdaterState = {
  //   status: UpdaterStatus.Running,
  //   percentage: 10,
  //   error: '1232',
  // };
  const updaterContext = useDependentComponentContextsStore(
    (state) => state.contexts,
  )?.find((s) => s.id == DependentComponentIds.BakabaseUpdater);
  const checkNewAppVersion = () => {
    BApi.updater.getNewAppVersion().then((a) => {
      setNewVersion(a.data || {});
    });
  };

  useEffect(() => {
    checkNewAppVersion();

    return () => { };
  }, []);

  const renderNewVersion = () => {
    // if (!updaterContext || updaterContext.status == DependentComponentStatus.NotInstalled) {
    //   return t<string>('Updater is required to auto-update app');
    // }

    // if (updaterContext.status == DependentComponentStatus.Installing) {
    //   return t<string>('We\'re installing updater, please wait');
    // }

    switch (appUpdaterState.status) {
      case UpdaterStatus.UpToDate:
        return t<string>("Up-to-date");
      case UpdaterStatus.Idle:
        if (newVersion) {
          if (newVersion.version) {
            return (
              <div className="flex items-center gap-2">
                <Chip variant="light" radius="sm">{newVersion.version}</Chip>
                {newVersion.changelog && (
                  <>
                    <Divider orientation={"vertical"} />
                    <Button
                      color={"secondary"}
                      size={"small"}
                      variant={"light"}
                      onPress={() => {
                        createPortal(Modal, {
                          size: "xl",
                          title: newVersion.version,
                          defaultVisible: true,
                          children: <pre>{newVersion.changelog}</pre>,
                          footer: { actions: ["cancel"] },
                        });
                      }}
                    >
                      {t<string>("Change log")}
                    </Button>
                  </>
                )}
                <Divider orientation={"vertical"} />
                <Button
                  color={"success"}
                  size={"small"}
                  variant={"light"}
                  onClick={() => {
                    BApi.updater.startUpdatingApp();
                  }}
                >
                  {t<string>("Click to auto-update")}
                </Button>
                {newVersion.installers?.length > 0 ? (
                  <>
                    <Divider orientation={"ver"} />
                    <Popover
                      trigger={
                        <Button
                          color={"primary"}
                          size={"sm"}
                          variant={"light"}
                        >
                          {t<string>(
                            "Auto-Update Fails? Click to download complete installers",
                          )}
                        </Button>
                      }
                      triggerType={"click"}
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
            return t<string>("Up-to-date");
          }
        } else {
          return t<string>("Failed to get latest version");
        }
      case UpdaterStatus.Running:
        return (
          <div style={{ display: "flex", alignItems: "center" }}>
            {newVersion?.version}
            &nbsp;
            {t<string>("Updating")}
            &nbsp;
            <div style={{ width: 400 }}>
              <Progress progressive percent={appUpdaterState.percentage} />
            </div>
          </div>
        );
      case UpdaterStatus.PendingRestart:
        return (
          <Button
            size={"small"}
            type={"primary"}
            onClick={() => {
              BApi.updater.restartAndUpdateApp();
            }}
          >
            {t<string>("Restart to update")}
          </Button>
        );
      case UpdaterStatus.Failed:
        return (
          <>
            {t<string>("Failed to update app")}:{" "}
            {t<string>(appUpdaterState.error)}
            &nbsp;
            <Button
              text
              type={"primary"}
              onClick={() => {
                BApi.updater.startUpdatingApp();
              }}
            >
              {t<string>("Click here to retry")}
            </Button>
          </>
        );
      default:
        return <Icon type={"loading"} />;
    }
  };
  const buildAppInfoDataSource = () =>
    [
      {
        label: "App Data Path",
        tip: "This is where core data files stored and DO NOT change them if not necessary.",
        value: (
          <Button
            color={"primary"}
            variant={"light"}
            onClick={() =>
              BApi.tool.openFileOrDirectory({ path: appInfo.appDataPath })
            }
          >
            {appInfo.appDataPath}
          </Button>
        ),
        // value: <Snippet hideSymbol>{appInfo.appDataPath}</Snippet>,
      },
      {
        label: "Temporary files path",
        tip: "It's a directory where temporary files stored, such as cover files, etc.",
        value: (
          <Button
            color={"primary"}
            variant={"light"}
            onClick={() =>
              BApi.tool.openFileOrDirectory({ path: appInfo.tempFilesPath })
            }
          >
            {appInfo.tempFilesPath}
          </Button>
        ),
      },
      {
        label: "Log Path",
        tip:
          "Detailed information which describing the running states of app." +
          " You can send log files to developer if the app is not running normally, and you can delete them also if everything is ok.",
        value: (
          <Button
            color={"primary"}
            variant={"light"}
            onClick={() =>
              BApi.tool.openFileOrDirectory({ path: appInfo.logPath })
            }
          >
            {appInfo.logPath}
          </Button>
        ),
      },
      {
        label: "Backup Path",
        tip: "A data backup will be created when using the new version of app first time, you can delete them if everything is ok.",
        value: (
          <Button
            color={"primary"}
            variant={"light"}
            onClick={() =>
              BApi.tool.openFileOrDirectory({ path: appInfo.backupPath })
            }
          >
            {appInfo.backupPath}
          </Button>
        ),
      },
      {
        label: "Core Version",
        value: (
          <Chip radius={"sm"} variant={"light"}>
            {appInfo.coreVersion}
          </Chip>
        ),
      },
      {
        label: "Latest version",
        value: renderNewVersion(),
      },
    ].map((x) => ({ ...x, label: t<string>(x.label), tip: t<string>(x.tip) }));

  return (
    <div className="group">
      {/* <Title title={t<string>('System information')} /> */}
      <div className="settings">
        <Table isCompact removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("System information")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {buildAppInfoDataSource().map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className={"hover:bg-[var(--bakaui-overlap-background)]"}
                >
                  <TableCell>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      {c.tip ? (
                        <Tooltip
                          color={"secondary"}
                          content={t<string>(c.tip)}
                          placement={"top"}
                        >
                          <div className={"flex items-center gap-1"}>
                            {t<string>(c.label)}
                            <AiOutlineQuestionCircle className={"text-base"} />
                          </div>
                        </Tooltip>
                      ) : (
                        t<string>(c.label)
                      )}
                    </div>
                  </TableCell>
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
