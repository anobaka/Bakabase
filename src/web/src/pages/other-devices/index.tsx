"use client";

import type {
  BakabaseServiceModelsViewClientAppDownloadsViewModel as ClientDownloads,
  BakabaseServiceModelsViewMobileAppDownloadsViewModel as MobileDownloads,
  BakabaseServiceModelsViewOtherDeviceDownloadsViewModel as Downloads,
} from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { QRCodeSVG } from "qrcode.react";
import {
  AiOutlineAndroid,
  AiOutlineApple,
  AiOutlineDesktop,
  AiOutlineWindows,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import ExternalLink from "@/components/ExternalLink";
import { Chip } from "@/components/bakaui";
import MigrationNotice from "@/features/federation/components/MigrationNotice";

/**
 * Where to get Bakabase for a device that is not this one.
 *
 * Not "the mobile app page": a phone and a second computer are two answers to one
 * question, and the reader asking it does not think of them as separate topics. What
 * differs is how the link reaches the device — a phone camera can take a QR code, while
 * the copy going to another computer has to be sendable — so the two halves offer
 * different affordances over the same data.
 *
 * Every URL here comes from a manifest CI wrote after publishing (see
 * scripts/desktop/build_client_manifest.py and its mobile twin). The page never composes
 * a download URL: doing so would leave it silently offering 404s the next time the
 * release pipeline renamed a file.
 */
const OtherDevicesPage = () => {
  const { t } = useTranslation();
  const [downloads, setDownloads] = useState<Downloads | null>();

  useEffect(() => {
    BApi.otherDevices
      .getOtherDeviceDownloads()
      .then((rsp) => setDownloads(rsp.data ?? null))
      .catch(() => setDownloads(null));
  }, []);

  if (downloads === undefined) {
    return <div className="p-6 text-sm">{t<string>("otherDevices.loading")}</div>;
  }

  const mobile = downloads?.mobile;
  const client = downloads?.desktopClient;

  return (
    <div className="p-6 flex flex-col gap-6 max-w-[880px]">
      <div>
        <div className="text-lg font-semibold">{t<string>("otherDevices.title")}</div>
        <div className="text-sm text-foreground-500 mt-2">{t<string>("otherDevices.intro")}</div>
      </div>

      <MigrationNotice />

      {!mobile && !client && (
        <div className="text-sm text-foreground-500">{t<string>("otherDevices.unavailable")}</div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {client && <DesktopClientCard downloads={client} />}
        {mobile && <MobileCard downloads={mobile} />}
      </div>
    </div>
  );
};

const formatSize = (size?: number) => (size ? `${(size / 1024 / 1024).toFixed(1)} MB` : "");

/** Version, publish date and a link to the notes — the same header on both cards. */
const CardHeader: React.FC<{
  icon: React.ReactNode;
  title: string;
  version?: string;
  publishedAt?: string | null;
  releaseUrl?: string | null;
}> = ({ icon, title, version, publishedAt, releaseUrl }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2 font-medium">
        {icon}
        {title}
      </div>
      <div className="flex items-center gap-2 text-xs text-foreground-500 flex-wrap">
        {version && (
          <Chip radius="sm" size="sm" variant="light">
            v{version}
          </Chip>
        )}
        {publishedAt && <span>{new Date(publishedAt).toLocaleDateString()}</span>}
        {releaseUrl && (
          <ExternalLink href={releaseUrl}>{t<string>("otherDevices.releaseNotes")}</ExternalLink>
        )}
      </div>
    </div>
  );
};

const DownloadLinks: React.FC<{ cdnUrl?: string | null; githubUrl?: string | null }> = ({
  cdnUrl,
  githubUrl,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex items-center gap-3 flex-wrap">
      {cdnUrl && <ExternalLink href={cdnUrl}>{t<string>("otherDevices.cdnLink")}</ExternalLink>}
      {githubUrl && (
        <ExternalLink href={githubUrl}>{t<string>("otherDevices.githubLink")}</ExternalLink>
      )}
    </div>
  );
};

/**
 * The thin client, for another computer.
 *
 * No QR code here on purpose. The reader is sitting at the machine running the server;
 * the copy they want is for a different computer, so what helps is a link they can send,
 * not one their phone can photograph.
 */
const DesktopClientCard: React.FC<{ downloads: ClientDownloads }> = ({ downloads }) => {
  const { t } = useTranslation();

  const platforms: { id: string; label: string; icon: React.ReactNode }[] = [
    { id: "win-x64", label: t("otherDevices.client.windows"), icon: <AiOutlineWindows /> },
    { id: "osx-arm64", label: t("otherDevices.client.macArm"), icon: <AiOutlineApple /> },
    { id: "osx-x64", label: t("otherDevices.client.macIntel"), icon: <AiOutlineApple /> },
  ];

  return (
    <div className="border rounded-lg p-4 flex flex-col gap-3">
      <CardHeader
        icon={<AiOutlineDesktop className="text-xl" />}
        publishedAt={downloads.publishedAt}
        releaseUrl={downloads.releaseUrl}
        title={t("federation.migration.legacy")}
        version={downloads.version}
      />

      <div className="text-sm text-foreground-500">{t<string>("otherDevices.client.intro")}</div>

      {/* The question this page gets asked from outside: the two downloads are named
          Bakabase and Bakabase Client, and nothing about the names says that one of them
          cannot work on its own. Said here because this is where someone goes to fetch
          the second one. */}
      <div className="text-sm border-l-2 border-primary/40 pl-3 text-foreground-500">
        {t<string>("otherDevices.client.vsAllInOne")}
      </div>

      {platforms.map((platform) => {
        const files = (downloads.files ?? []).filter((f) => f.platform === platform.id);

        if (files.length === 0) {
          return null;
        }

        return (
          <div key={platform.id} className="flex flex-col gap-1">
            <div className="flex items-center gap-1 text-sm">
              {platform.icon}
              {platform.label}
            </div>
            {files.map((file) => (
              <div key={file.name} className="flex flex-col gap-0.5 pl-5">
                <div className="text-xs text-foreground-500">
                  {t<string>(
                    file.shape === "portable"
                      ? "otherDevices.client.portable"
                      : "otherDevices.client.setup",
                  )}
                  <span className="ml-2 text-foreground-400">{formatSize(file.size)}</span>
                </div>
                <DownloadLinks cdnUrl={file.cdnUrl} githubUrl={file.githubUrl} />
              </div>
            ))}
          </div>
        );
      })}

      {/* Said out loud: a Linux reader finding nothing here should know it is absent by
          design, not that the page failed to load. */}
      <div className="text-xs text-foreground-400">
        {t<string>("otherDevices.client.platforms")}
      </div>
    </div>
  );
};

/** Phones and tablets. QR codes, because the device with the camera is the target. */
const MobileCard: React.FC<{ downloads: MobileDownloads }> = ({ downloads }) => {
  const { t } = useTranslation();

  const androidFiles = (downloads.files ?? []).filter((f) => f.platform?.startsWith("android"));
  const iosFile = (downloads.files ?? []).find((f) => f.platform === "ios");
  const primaryApk =
    androidFiles.find((f) => f.platform === "android-arm64-v8a") ?? androidFiles[0];

  return (
    <div className="border rounded-lg p-4 flex flex-col gap-3">
      <CardHeader
        icon={<AiOutlineAndroid className="text-xl" />}
        publishedAt={downloads.publishedAt}
        releaseUrl={downloads.releaseUrl}
        title={t("otherDevices.mobile.title")}
        version={downloads.version}
      />

      <div className="text-sm text-foreground-500">{t<string>("otherDevices.mobile.intro")}</div>

      <div className="flex items-center gap-2 text-sm font-medium">
        <AiOutlineAndroid className="text-lg" />
        Android
      </div>
      {primaryApk?.cdnUrl && (
        <div className="flex items-start gap-4">
          <QRCodeSVG size={112} value={primaryApk.cdnUrl} />
          <div className="text-sm text-foreground-500">
            {t<string>("otherDevices.mobile.androidQrHint")}
          </div>
        </div>
      )}
      {androidFiles.map((file) => (
        <div key={file.name} className="flex flex-col gap-1">
          <div className="text-sm">
            {file.platform?.replace("android-", "")}
            {file.platform === "android-arm64-v8a" && (
              <Chip className="ml-2" color="primary" size="sm" variant="flat">
                {t<string>("otherDevices.recommended")}
              </Chip>
            )}
            <span className="ml-2 text-foreground-400">{formatSize(file.size)}</span>
          </div>
          <DownloadLinks cdnUrl={file.cdnUrl} githubUrl={file.githubUrl} />
        </div>
      ))}

      <div className="flex items-center gap-2 text-sm font-medium mt-2">
        <AiOutlineApple className="text-lg" />
        iOS
      </div>
      {downloads.sidestoreSourceUrl && (
        <>
          <div className="flex items-start gap-4">
            <QRCodeSVG size={112} value={downloads.sidestoreSourceUrl} />
            <div className="text-sm text-foreground-500">
              {t<string>("otherDevices.mobile.iosSourceHint")}
            </div>
          </div>
          <div className="flex flex-col gap-1">
            <div className="text-sm">{t<string>("otherDevices.mobile.sidestoreSource")}</div>
            <ExternalLink href={downloads.sidestoreSourceUrl}>
              {t<string>("otherDevices.mobile.sidestoreSourceLink")}
            </ExternalLink>
          </div>
        </>
      )}
      {iosFile && (
        <div className="flex flex-col gap-1">
          <div className="text-sm">
            {t<string>("otherDevices.mobile.unsignedIpa")}
            <span className="ml-2 text-foreground-400">{formatSize(iosFile.size)}</span>
          </div>
          <DownloadLinks cdnUrl={iosFile.cdnUrl} githubUrl={iosFile.githubUrl} />
        </div>
      )}
      <div className="text-xs text-foreground-400">
        {t<string>("otherDevices.mobile.iosLimits")}
      </div>
    </div>
  );
};

OtherDevicesPage.displayName = "OtherDevicesPage";

export default OtherDevicesPage;
