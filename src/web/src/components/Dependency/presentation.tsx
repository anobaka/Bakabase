import type { ComponentType } from "react";
import type { TFunction } from "i18next";
import {
  AppstoreOutlined,
  CloudDownloadOutlined,
  FileZipOutlined,
  GlobalOutlined,
  CodeOutlined,
  VideoCameraOutlined,
} from "@ant-design/icons";
import { DependentComponentStatus } from "@/sdk/constants";
import type { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";

export type DependencyContext = ReturnType<
  typeof useDependentComponentContextsStore.getState
>["contexts"][number];
export const LocaleEmulatorId = "locale-emulator-component-service";
export const PythonRuntimeId = "cloud-python-component-service";
export function dependencyDisplayName(context: DependencyContext, t: TFunction): string {
  return context.id === PythonRuntimeId ? t<string>("dependencies.python.name") : context.name;
}
export const dependencyGroups = ["media", "download", "runtime", "compatibility", "other"] as const;
export type DependencyGroup = (typeof dependencyGroups)[number];
export type DependencyFilter = "all" | "attention" | "installed" | "missing" | "installing";
export interface DependencyCheckInfo {
  checking?: boolean;
  installRequested?: boolean;
  latestVersion?: { canUpdate: boolean; installedVersionRecognized?: boolean };
  failure?: { stage: string; details: string };
}
interface Presentation {
  group: DependencyGroup;
  purposeKey?: string;
  Icon: ComponentType<{ className?: string }>;
}
const presentation: Record<string, Presentation> = {
  "364e3884-4c6f-446f-b72c-1ec84e8da2c2": {
    group: "media",
    purposeKey: "configuration.dependency.videoPurpose",
    Icon: VideoCameraOutlined,
  },
  "7z-archiver-component-service": {
    group: "media",
    purposeKey: "configuration.dependency.archivePurpose",
    Icon: FileZipOutlined,
  },
  [LocaleEmulatorId]: {
    group: "compatibility",
    purposeKey: "configuration.dependency.localePurpose",
    Icon: GlobalOutlined,
  },
  "pyzipper-component-service": {
    group: "compatibility",
    Icon: FileZipOutlined,
  },
  "baidu-pcs-go-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.baidu",
    Icon: CloudDownloadOutlined,
  },
  "gdown-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.gdown",
    Icon: CloudDownloadOutlined,
  },
  "gallery-dl-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.galleryDl",
    Icon: CloudDownloadOutlined,
  },
  "rclone-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.rclone",
    Icon: CloudDownloadOutlined,
  },
  "megacmd-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.megacmd",
    Icon: CloudDownloadOutlined,
  },
  "pikpak-cli-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.pikpakCli",
    Icon: CloudDownloadOutlined,
  },
  "terabox-cli-component-service": {
    group: "download",
    purposeKey: "dependencies.purpose.teraboxCli",
    Icon: CloudDownloadOutlined,
  },
  "cloud-python-component-service": {
    group: "runtime",
    purposeKey: "dependencies.purpose.python",
    Icon: CodeOutlined,
  },
};
export function dependencyPresentation(context: DependencyContext, t: TFunction) {
  const entry = presentation[context.id] ?? { group: "other" as const, Icon: AppstoreOutlined };
  return {
    ...entry,
    purpose: entry.purposeKey ? t<string>(entry.purposeKey) : (context.description ?? ""),
  };
}
export function isDependencyInstalling(context: DependencyContext, entry?: DependencyCheckInfo) {
  return context.status === DependentComponentStatus.Installing || !!entry?.installRequested;
}
/** Optional components are only actionable after a failed check/install or an available update. */
export function dependencyNeedsAttention(context: DependencyContext, entry?: DependencyCheckInfo) {
  if (!context.isAvailableOnCurrentPlatform || isDependencyInstalling(context, entry)) return false;
  if (context.error || entry?.failure) return true;
  const installed = context.status === DependentComponentStatus.Installed;
  return (
    (!installed && context.isRequired) ||
    (installed &&
      !!entry?.latestVersion &&
      (entry.latestVersion.canUpdate || entry.latestVersion.installedVersionRecognized === false))
  );
}
export function matchesDependency(
  context: DependencyContext,
  query: string,
  filter: DependencyFilter,
  t: TFunction,
  entry?: DependencyCheckInfo,
) {
  const matchesQuery = dependencySearchText(context, t)
    .toLocaleLowerCase()
    .includes(query.trim().toLocaleLowerCase());
  if (!matchesQuery) return false;
  if (filter === "attention") return dependencyNeedsAttention(context, entry);
  if (filter === "installing") return isDependencyInstalling(context, entry);
  if (filter === "installed")
    return (
      context.status === DependentComponentStatus.Installed &&
      !isDependencyInstalling(context, entry)
    );
  if (filter === "missing")
    return (
      context.isAvailableOnCurrentPlatform &&
      context.status === DependentComponentStatus.NotInstalled &&
      !isDependencyInstalling(context, entry)
    );
  return true;
}
export function dependencySearchText(context: DependencyContext, t: TFunction): string {
  const meta = dependencyPresentation(context, t);
  const targets = (context.supportedTargets ?? [])
    .map(
      (target) =>
        `${t(`dependencies.os.${target.os}`, { defaultValue: target.os })} ${target.os} ${target.architecture} ${target.runtime ?? ""}`,
    )
    .join(" ");
  return `${context.name} ${dependencyDisplayName(context, t)} ${context.description ?? ""} ${meta.purpose} ${t(`dependencies.group.${meta.group}`)} ${targets}`;
}
export function summarizeDependencies(
  contexts: DependencyContext[],
  entries: Record<string, DependencyCheckInfo> = {},
) {
  return {
    installed: contexts.filter(
      (context) =>
        context.status === DependentComponentStatus.Installed &&
        !isDependencyInstalling(context, entries[context.id]),
    ).length,
    installing: contexts.filter((context) => isDependencyInstalling(context, entries[context.id]))
      .length,
    attention: contexts.filter((context) => dependencyNeedsAttention(context, entries[context.id]))
      .length,
  };
}
