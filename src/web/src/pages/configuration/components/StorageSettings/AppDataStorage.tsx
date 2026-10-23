import type { TFunction } from "i18next";
import type { BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo } from "@/sdk/Api";
import type { SettingItem } from "../SettingsSection";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { WarningOutlined } from "@ant-design/icons";

import IdentityRecoveryLink from "../AppInfo/IdentityRecoveryLink";
import AppDataImportButton from "../AppInfo/Import";
import { RelocationButton, RelocationRestartGate } from "../AppInfo/Relocation";
import { LegacyAppDataNoticeBanner } from "../AppInfo/LegacyNotice";
import { SettingsRows } from "../SettingsSection";

import StorageUsageButton from "./StorageUsage";

import { DataPathSource } from "@/sdk/constants";
import FilePathValue from "@/components/FilePathValue";
import { Accordion, AccordionItem, Chip, Divider } from "@/components/bakaui";

export function getAppDataStorageItems(
  appInfo: Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>,
  t: TFunction,
): SettingItem[] {
  const renderPathValue = (path?: string, description?: string) => (
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

  const items: (Omit<SettingItem, "label" | "render"> & {
    label: string;
    value: React.ReactNode;
  })[] = [
    {
      id: "appDataPath",
      label: "configuration.appInfo.appDataPath",
      keywords: [
        "path",
        "directory",
        "folder",
        "import",
        "数据",
        "目录",
        "导入",
        "已有数据",
        "服务器",
        "一体版",
        "usage",
        "space",
        "空间",
        "占用",
        "统计",
      ],
      value: (
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-1 flex-wrap">
            <FilePathValue path={appInfo.appDataPath} />
            {renderDataPathSource()}
            <AppDataImportButton />
            <StorageUsageButton />
            {appInfo.appDataPath &&
              !appInfo.dataInSystemPath &&
              appInfo.dataPathSource !== DataPathSource.Environment && (
                <>
                  <Divider className="mx-1" orientation="vertical" />
                  <RelocationButton />
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
              {appInfo.dataPathSource !== DataPathSource.Environment && <RelocationButton />}
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
  ];

  return items.map(({ value, ...item }) => ({
    ...item,
    label: t(item.label),
    render: () => value,
  }));
}

/** The same server-owned storage controls work in desktop, Docker and managed views. */
export default function AppDataStorage({
  primaryItems,
  detailItems,
  query = "",
}: {
  primaryItems: SettingItem[];
  detailItems: SettingItem[];
  query?: string;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const expandedBySearch = !!query && detailItems.length > 0;

  return (
    <>
      <RelocationRestartGate />
      <div className="px-4">
        <LegacyAppDataNoticeBanner />
      </div>
      {primaryItems.length > 0 && <SettingsRows items={primaryItems} />}
      {detailItems.length > 0 && (
        <Accordion
          className="px-0"
          selectedKeys={expanded || expandedBySearch ? new Set(["storage"]) : new Set()}
          onSelectionChange={(keys) => setExpanded(keys === "all" || keys.has("storage"))}
        >
          <AccordionItem
            key="storage"
            aria-label={t("configuration.storage.details")}
            classNames={{
              trigger: "px-4 py-2.5",
              title: "text-sm text-foreground-600",
              content: "pt-0 pb-2",
            }}
            title={t("configuration.storage.details")}
          >
            <SettingsRows items={detailItems} />
          </AccordionItem>
        </Accordion>
      )}
    </>
  );
}
