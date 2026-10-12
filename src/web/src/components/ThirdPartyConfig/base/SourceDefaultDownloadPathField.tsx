"use client";

import type { ThirdPartyId } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import DownloadDirectoryField from "./DownloadDirectoryField";
import useEffectiveDownloadPath from "./useEffectiveDownloadPath";

import { useDownloaderGlobalOptionsStore } from "@/stores/options";

type Props = {
  thirdPartyId: ThirdPartyId;
  value?: string | null;
  onChange: (path: string) => void;
};

export default function SourceDefaultDownloadPathField({ thirdPartyId, value, onChange }: Props) {
  const { t } = useTranslation();
  const globalPath = useDownloaderGlobalOptionsStore((s) => s.data?.defaultDownloadPath);
  const inheritedPath = useEffectiveDownloadPath(thirdPartyId, value);
  const hasOverride = !!value?.trim();

  return (
    <DownloadDirectoryField
      clearLabel={t("downloader.config.sourceDefaultPath.reset")}
      description={
        hasOverride ? (
          t("downloader.config.sourceDefaultPath.overrideDescription")
        ) : inheritedPath ? (
          <>
            <span>{t("downloader.config.sourceDefaultPath.inherited")}</span>{" "}
            <code className="break-all">{inheritedPath}</code>
          </>
        ) : (
          t("downloader.config.sourceDefaultPath.inheritDescription")
        )
      }
      label={t("thirdPartyConfig.field.defaultPath.label")}
      placeholder={t("downloader.config.sourceDefaultPath.inherit")}
      startPath={globalPath ?? undefined}
      value={value}
      onChange={onChange}
    />
  );
}
