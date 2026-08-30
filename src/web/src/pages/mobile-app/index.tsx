"use client";

import type { BakabaseServiceModelsViewMobileAppDownloadsViewModel } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { QRCodeSVG } from "qrcode.react";
import { AiOutlineAndroid, AiOutlineApple } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import ExternalLink from "@/components/ExternalLink";
import { Chip } from "@/components/bakaui";

/**
 * Download hub for the mobile companion app. The URLs are produced by CI, so
 * they arrive through the server's cached copy of the download manifest —
 * see MobileAppDownloadService. Every file offers both the Aliyun CDN and the
 * GitHub asset link; QR codes carry the CDN link for phone cameras.
 */
const MobileAppPage = () => {
  const { t } = useTranslation();
  const [downloads, setDownloads] =
    useState<BakabaseServiceModelsViewMobileAppDownloadsViewModel | null>();

  useEffect(() => {
    BApi.mobileApp
      .getMobileAppDownloads()
      .then((rsp) => setDownloads(rsp.data ?? null))
      .catch(() => setDownloads(null));
  }, []);

  if (downloads === undefined) {
    return <div className="p-6 text-sm">{t("mobileApp.loading")}</div>;
  }

  if (downloads === null) {
    return (
      <div className="p-6 flex flex-col gap-2 max-w-[640px]">
        <div className="text-lg font-semibold">{t("mobileApp.title")}</div>
        <div className="text-sm text-foreground-500">{t("mobileApp.unavailable")}</div>
      </div>
    );
  }

  const androidFiles = (downloads.files ?? []).filter((f) =>
    f.platform?.startsWith("android"),
  );
  const iosFile = (downloads.files ?? []).find((f) => f.platform === "ios");
  const primaryApk =
    androidFiles.find((f) => f.platform === "android-arm64-v8a") ?? androidFiles[0];

  const formatSize = (size?: number) =>
    size ? `${(size / 1024 / 1024).toFixed(1)} MB` : "";

  const renderLinks = (cdnUrl?: string | null, githubUrl?: string | null) => (
    <div className="flex items-center gap-3 flex-wrap">
      {cdnUrl && <ExternalLink href={cdnUrl}>{t("mobileApp.cdnLink")}</ExternalLink>}
      {githubUrl && (
        <ExternalLink href={githubUrl}>{t("mobileApp.githubLink")}</ExternalLink>
      )}
    </div>
  );

  return (
    <div className="p-6 flex flex-col gap-6 max-w-[880px]">
      <div>
        <div className="text-lg font-semibold">{t("mobileApp.title")}</div>
        <div className="flex items-center gap-2 mt-1 text-sm text-foreground-500">
          <Chip size="sm" variant="flat">
            v{downloads.version}
          </Chip>
          {downloads.publishedAt && (
            <span>{new Date(downloads.publishedAt).toLocaleDateString()}</span>
          )}
          {downloads.releaseUrl && (
            <ExternalLink href={downloads.releaseUrl}>
              {t("mobileApp.releaseNotes")}
            </ExternalLink>
          )}
        </div>
        <div className="text-sm text-foreground-500 mt-2">{t("mobileApp.intro")}</div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="border rounded-lg p-4 flex flex-col gap-3">
          <div className="flex items-center gap-2 font-medium">
            <AiOutlineAndroid className="text-xl" />
            Android
          </div>
          {primaryApk?.cdnUrl && (
            <div className="flex items-start gap-4">
              <QRCodeSVG size={112} value={primaryApk.cdnUrl} />
              <div className="text-sm text-foreground-500">
                {t("mobileApp.androidQrHint")}
              </div>
            </div>
          )}
          {androidFiles.map((file) => (
            <div key={file.name} className="flex flex-col gap-1">
              <div className="text-sm">
                {file.platform?.replace("android-", "")}
                {file.platform === "android-arm64-v8a" && (
                  <Chip className="ml-2" color="primary" size="sm" variant="flat">
                    {t("mobileApp.recommended")}
                  </Chip>
                )}
                <span className="ml-2 text-foreground-400">{formatSize(file.size)}</span>
              </div>
              {renderLinks(file.cdnUrl, file.githubUrl)}
            </div>
          ))}
        </div>

        <div className="border rounded-lg p-4 flex flex-col gap-3">
          <div className="flex items-center gap-2 font-medium">
            <AiOutlineApple className="text-xl" />
            iOS
          </div>
          {downloads.sidestoreSourceUrl && (
            <div className="flex items-start gap-4">
              <QRCodeSVG size={112} value={downloads.sidestoreSourceUrl} />
              <div className="text-sm text-foreground-500">
                {t("mobileApp.iosSourceHint")}
              </div>
            </div>
          )}
          {downloads.sidestoreSourceUrl && (
            <div className="flex flex-col gap-1">
              <div className="text-sm">{t("mobileApp.sidestoreSource")}</div>
              <ExternalLink href={downloads.sidestoreSourceUrl}>
                {t("mobileApp.sidestoreSourceLink")}
              </ExternalLink>
            </div>
          )}
          {iosFile && (
            <div className="flex flex-col gap-1">
              <div className="text-sm">
                {t("mobileApp.unsignedIpa")}
                <span className="ml-2 text-foreground-400">{formatSize(iosFile.size)}</span>
              </div>
              {renderLinks(iosFile.cdnUrl, iosFile.githubUrl)}
            </div>
          )}
          <div className="text-xs text-foreground-400">{t("mobileApp.iosLimits")}</div>
        </div>
      </div>
    </div>
  );
};

MobileAppPage.displayName = "MobileAppPage";

export default MobileAppPage;
