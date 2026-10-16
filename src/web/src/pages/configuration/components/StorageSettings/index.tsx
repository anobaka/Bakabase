import type { BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import { filterItems, normalizeQuery, useSettingsSearchMatch } from "../SettingsSection";

import AppDataStorage, { getAppDataStorageItems } from "./AppDataStorage";

import ProfilesSettings, { matchesProfilesSettings } from "@/features/profiles/ProfilesSettings";
import { useDesktopProfiles } from "@/features/profiles/useDesktopProfiles";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

const storageKeywords = ["storage", "data", "数据", "存储"];
const workspaceKeywords = [
  "profiles",
  "profile",
  "workspace",
  "workspaces",
  "appdata",
  "工作区",
  "数据空间",
];

export default function StorageSettings({
  appInfo,
  query = "",
}: {
  appInfo: Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>;
  query?: string;
}) {
  const { t } = useTranslation();
  const profiles = useDesktopProfiles();
  const isManagedView = useIsPureClient();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const title = t(
    profiles.localDesktop
      ? "profiles.title"
      : isManagedView
        ? "configuration.storage.title.server"
        : "configuration.storage.title",
  );
  const normalizedQuery = normalizeQuery(query);
  const keywords = profiles.localDesktop
    ? [...storageKeywords, ...workspaceKeywords]
    : storageKeywords;
  const sectionMatches =
    !normalizedQuery || [title, ...keywords].join(" ").toLowerCase().includes(normalizedQuery);
  const items = getAppDataStorageItems(appInfo, t);

  if (profiles.localDesktop) {
    const dataDirectory = items.find((item) => item.id === "appDataPath");

    if (dataDirectory) dataDirectory.label = t("profiles.currentDirectory");
  }
  const primaryItems = filterItems(
    title,
    items.filter((item) => item.id === "appDataPath"),
    normalizedQuery,
    keywords,
  );
  const detailItems = filterItems(
    t("configuration.storage.details"),
    items.filter((item) => item.id !== "appDataPath"),
    sectionMatches ? "" : normalizedQuery,
  );
  const managementMatches =
    profiles.localDesktop &&
    (profiles.state?.available || profiles.error) &&
    (sectionMatches || matchesProfilesSettings(normalizedQuery, t, profiles.state?.profiles || []));
  const matched = primaryItems.length > 0 || detailItems.length > 0 || !!managementMatches;

  useSettingsSearchMatch("workspace-storage", matched);
  if (!matched) return null;

  return (
    <section
      aria-label={title}
      className="overflow-hidden rounded-large border border-default-200 dark:border-default-100"
    >
      <h2 className="bg-default-100/60 px-4 py-2 text-sm font-semibold dark:bg-default-50/40">
        {title}
      </h2>
      {profiles.localDesktop && (
        <p className="px-4 pt-3 pb-1 text-sm text-default-500">{t("profiles.description")}</p>
      )}
      {isManagedView && (
        <p className="px-4 pt-3 pb-1 text-sm text-default-500">
          {t("configuration.storage.serverDescription")}
          {serverName && ` ${serverName}`}
        </p>
      )}
      {managementMatches && (
        <ProfilesSettings controller={profiles} query={sectionMatches ? "" : normalizedQuery} />
      )}
      <AppDataStorage
        detailItems={detailItems}
        primaryItems={primaryItems}
        query={normalizedQuery}
      />
    </section>
  );
}
