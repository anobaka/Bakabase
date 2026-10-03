"use client";

import type { ReactNode } from "react";
import type {
  BakabaseServiceModelsViewMobileAppDownloadsViewModel as MobileDownloads,
  BakabaseServiceModelsViewMobileAppDownloadFileViewModel as DownloadFile,
  BakabaseServiceModelsViewOtherDeviceDownloadsViewModel as Downloads,
} from "@/sdk/Api";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { QRCodeSVG } from "qrcode.react";
import {
  AiOutlineAndroid,
  AiOutlineApple,
  AiOutlineCloudServer,
  AiOutlineDesktop,
  AiOutlineDownload,
  AiOutlineInfoCircle,
  AiOutlineMobile,
  AiOutlineReload,
} from "react-icons/ai";
import { FaDocker, FaWindows } from "react-icons/fa";

import BApi from "@/sdk/BApi";
import ExternalLink from "@/components/ExternalLink";
import { Button, Chip } from "@/components/bakaui";

const releasesUrl = "https://github.com/anobaka/Bakabase/releases";
const dockerHubUrl = "https://hub.docker.com/r/anobaka/bakabase";
const dockerGuideUrl = "https://github.com/anobaka/Bakabase/blob/main/docs/docker-installation.md";
const dockerCommand = [
  "docker run -d --name bakabase --restart unless-stopped \\",
  "  --platform linux/amd64 \\",
  "  -p 34567:34567 \\",
  "  -e API_LISTENING_PORTS=34567 \\",
  "  -e BAKABASE_DATA_DIR=/data \\",
  "  -v bakabase-data:/data \\",
  '  -v "$PWD/media:/media" \\',
  '  "anobaka/bakabase:<VERSION>"',
].join("\n");

/** Mobile package URLs come from the published manifest, never inferred filenames. */
const OtherDevicesPage = () => {
  const { t } = useTranslation();
  const [downloads, setDownloads] = useState<Downloads | null>();
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await BApi.otherDevices.getOtherDeviceDownloads();

      setDownloads(response.code ? null : (response.data ?? null));
    } catch {
      setDownloads(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const mobile = downloads?.mobile;

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-5 p-4 md:p-6">
      <header>
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <AiOutlineDownload aria-hidden className="text-primary" />
          {t<string>("otherDevices.title")}
        </h1>
        <p className="mt-2 max-w-3xl text-sm leading-relaxed text-default-500">
          {t<string>("otherDevices.intro")}
        </p>
      </header>

      <DownloadSection
        icon={<AiOutlineDesktop aria-hidden />}
        id="desktop-downloads"
        subtitle={t("otherDevices.desktop.subtitle")}
        title={t("otherDevices.desktop.title")}
      >
        <div className="flex flex-col justify-between gap-5 lg:flex-row lg:items-center">
          <div className="flex min-w-0 flex-col gap-3">
            <p className="max-w-2xl text-sm leading-relaxed text-default-500">
              {t<string>("otherDevices.desktop.intro")}
            </p>
            <div className="flex flex-wrap gap-2">
              <Chip size="sm" startContent={<FaWindows aria-hidden />} variant="flat">
                Windows · x64
              </Chip>
              <Chip size="sm" startContent={<AiOutlineApple aria-hidden />} variant="flat">
                macOS · Intel / Apple Silicon
              </Chip>
              <Chip color="primary" size="sm" variant="flat">
                {t<string>("otherDevices.canShare")}
              </Chip>
            </div>
          </div>
          <div className="flex shrink-0 flex-wrap items-center gap-3">
            <ExternalLink
              className="min-h-10 rounded-xl bg-primary px-4 text-sm font-medium text-primary-foreground"
              href={`${releasesUrl}/latest`}
            >
              <AiOutlineDownload aria-hidden />
              {t<string>("otherDevices.desktop.stable")}
            </ExternalLink>
            <ExternalLink className="text-sm" href={releasesUrl}>
              {t<string>("otherDevices.desktop.beta")}
            </ExternalLink>
          </div>
        </div>
      </DownloadSection>

      <DownloadSection
        icon={<AiOutlineMobile aria-hidden />}
        id="mobile-downloads"
        subtitle={t("otherDevices.mobile.subtitle")}
        title={t("otherDevices.mobile.title")}
      >
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="max-w-2xl text-sm leading-relaxed text-default-500">
              {t<string>("otherDevices.mobile.intro")}
            </p>
            {mobile && <ReleaseInfo downloads={mobile} />}
          </div>
          <p className="flex items-start gap-2 rounded-xl bg-warning-50 px-3 py-2.5 text-sm text-warning-700">
            <AiOutlineInfoCircle aria-hidden className="mt-0.5 shrink-0 text-base" />
            {t<string>("otherDevices.mobile.noSharing")}
          </p>
          {mobile ? (
            <MobilePackages downloads={mobile} />
          ) : (
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-default-50 p-4">
              <p className="max-w-2xl text-sm text-default-500" role="status">
                {t<string>(loading ? "otherDevices.loading" : "otherDevices.unavailable")}
              </p>
              {!loading && (
                <Button
                  size="sm"
                  startContent={<AiOutlineReload aria-hidden />}
                  variant="flat"
                  onPress={() => void load()}
                >
                  {t<string>("otherDevices.retry")}
                </Button>
              )}
            </div>
          )}
        </div>
      </DownloadSection>

      <DownloadSection
        icon={<FaDocker aria-hidden />}
        id="docker-downloads"
        subtitle={t("otherDevices.docker.subtitle")}
        title={t("otherDevices.docker.title")}
      >
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex flex-col gap-3">
              <p className="max-w-2xl text-sm leading-relaxed text-default-500">
                {t<string>("otherDevices.docker.intro")}
              </p>
              <div className="flex flex-wrap gap-2">
                <Chip size="sm" startContent={<AiOutlineCloudServer aria-hidden />} variant="flat">
                  NAS / Linux · x64
                </Chip>
                <Chip color="primary" size="sm" variant="flat">
                  {t<string>("otherDevices.canShare")}
                </Chip>
              </div>
            </div>
            <ExternalLink className="text-sm" href={dockerHubUrl}>
              Docker Hub
            </ExternalLink>
          </div>
          <details className="rounded-xl border border-default-200 bg-default-50">
            <summary className="cursor-pointer rounded-xl px-4 py-3 text-sm font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary">
              {t<string>("otherDevices.docker.instructions")}
            </summary>
            <div className="flex min-w-0 flex-col gap-3 border-t border-default-200 p-4 text-sm leading-relaxed text-default-500">
              <p>{t<string>("otherDevices.docker.installHint")}</p>
              <pre className="overflow-x-auto rounded-lg bg-content1 p-3 text-xs text-foreground">
                <code>{dockerCommand}</code>
              </pre>
              <p>{t<string>("otherDevices.docker.openHint")}</p>
              <ExternalLink className="self-start text-sm" href={dockerGuideUrl}>
                {t<string>("otherDevices.docker.guide")}
              </ExternalLink>
            </div>
          </details>
        </div>
      </DownloadSection>
    </div>
  );
};

const DownloadSection = ({
  id,
  icon,
  title,
  subtitle,
  children,
}: {
  id: string;
  icon: ReactNode;
  title: string;
  subtitle: string;
  children: ReactNode;
}) => (
  <section
    aria-labelledby={`${id}-title`}
    className="overflow-hidden rounded-2xl border border-default-200 bg-content1 shadow-sm"
  >
    <div className="flex items-center gap-3 border-b border-default-100 px-5 py-4">
      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary-50 text-2xl text-primary">
        {icon}
      </div>
      <div>
        <h2 className="text-base font-semibold" id={`${id}-title`}>
          {title}
        </h2>
        <p className="mt-0.5 text-xs text-default-500">{subtitle}</p>
      </div>
    </div>
    <div className="p-5">{children}</div>
  </section>
);

const ReleaseInfo = ({ downloads }: { downloads: MobileDownloads }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
      {downloads.version && (
        <Chip size="sm" variant="flat">
          v{downloads.version}
        </Chip>
      )}
      {downloads.publishedAt && <span>{new Date(downloads.publishedAt).toLocaleDateString()}</span>}
      {downloads.releaseUrl && (
        <ExternalLink href={downloads.releaseUrl} size="sm">
          {t<string>("otherDevices.releaseNotes")}
        </ExternalLink>
      )}
    </div>
  );
};

const DownloadLinks = ({ file }: { file: DownloadFile }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap items-center gap-3 text-sm">
      {file.cdnUrl && (
        <ExternalLink href={file.cdnUrl}>{t<string>("otherDevices.cdnLink")}</ExternalLink>
      )}
      {file.githubUrl && (
        <ExternalLink href={file.githubUrl}>{t<string>("otherDevices.githubLink")}</ExternalLink>
      )}
    </div>
  );
};

const MobilePackages = ({ downloads }: { downloads: MobileDownloads }) => {
  const { t } = useTranslation();
  const androidFiles = (downloads.files ?? []).filter((file) =>
    file.platform?.startsWith("android"),
  );
  const iosFile = (downloads.files ?? []).find((file) => file.platform === "ios");
  const primaryApk =
    androidFiles.find((file) => file.platform === "android-arm64-v8a") ?? androidFiles[0];
  const androidQrUrl = primaryApk?.cdnUrl || primaryApk?.githubUrl;
  const iosQrUrl = downloads.sidestoreSourceUrl || iosFile?.cdnUrl || iosFile?.githubUrl;

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <div className="flex min-w-0 flex-col gap-4 rounded-xl bg-default-50 p-4">
        <h3 className="flex items-center gap-2 text-sm font-semibold">
          <AiOutlineAndroid aria-hidden className="text-xl text-success" />
          Android
        </h3>
        {androidQrUrl && (
          <QrDownload
            hint={t("otherDevices.mobile.androidQrHint")}
            title="Android"
            url={androidQrUrl}
          />
        )}
        {androidFiles.map((file) => (
          <div key={file.name} className="flex flex-col gap-2 border-t border-default-200 pt-3">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="font-medium">{file.platform?.replace("android-", "")}</span>
              {file.platform === "android-arm64-v8a" && (
                <Chip color="primary" size="sm" variant="flat">
                  {t<string>("otherDevices.recommended")}
                </Chip>
              )}
              <FileSize file={file} />
            </div>
            <DownloadLinks file={file} />
          </div>
        ))}
        {!androidFiles.length && (
          <p className="text-sm text-default-500">{t<string>("otherDevices.mobile.noPackage")}</p>
        )}
      </div>

      <div className="flex min-w-0 flex-col gap-4 rounded-xl bg-default-50 p-4">
        <h3 className="flex items-center gap-2 text-sm font-semibold">
          <AiOutlineApple aria-hidden className="text-xl" />
          iOS
        </h3>
        {iosQrUrl && (
          <QrDownload
            hint={t(
              downloads.sidestoreSourceUrl
                ? "otherDevices.mobile.iosSourceHint"
                : "otherDevices.mobile.iosIpaHint",
            )}
            title="iOS"
            url={iosQrUrl}
          />
        )}
        {downloads.sidestoreSourceUrl && (
          <div className="flex flex-col gap-2 border-t border-default-200 pt-3 text-sm">
            <span className="font-medium">{t<string>("otherDevices.mobile.sidestoreSource")}</span>
            <ExternalLink href={downloads.sidestoreSourceUrl}>
              {t<string>("otherDevices.mobile.sidestoreSourceLink")}
            </ExternalLink>
          </div>
        )}
        {iosFile && (
          <div className="flex flex-col gap-2 border-t border-default-200 pt-3">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="font-medium">{t<string>("otherDevices.mobile.unsignedIpa")}</span>
              <FileSize file={iosFile} />
            </div>
            <DownloadLinks file={iosFile} />
          </div>
        )}
        {!iosQrUrl && (
          <p className="text-sm text-default-500">{t<string>("otherDevices.mobile.noPackage")}</p>
        )}
        <p className="text-xs leading-relaxed text-default-500">
          {t<string>("otherDevices.mobile.iosLimits")}
        </p>
      </div>
    </div>
  );
};

const FileSize = ({ file }: { file: DownloadFile }) =>
  file.size ? (
    <span className="text-xs text-default-400">{(file.size / 1024 / 1024).toFixed(1)} MB</span>
  ) : null;

const QrDownload = ({ url, title, hint }: { url: string; title: string; hint: string }) => (
  <div className="flex items-start gap-4">
    <div className="shrink-0 rounded-xl bg-white p-2">
      <QRCodeSVG size={96} title={title} value={url} />
    </div>
    <p className="min-w-0 text-sm leading-relaxed text-default-500">{hint}</p>
  </div>
);

OtherDevicesPage.displayName = "OtherDevicesPage";

export default OtherDevicesPage;
