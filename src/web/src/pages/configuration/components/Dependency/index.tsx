"use client";

import { useTranslation } from "react-i18next";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";
import { Spinner } from "@/components/bakaui";
import SettingsSection, {
  type SettingItem,
} from "@/pages/configuration/components/SettingsSection";
import {
  dependencySearchText,
  dependencyDisplayName,
  LocaleEmulatorId,
  matchesDependency,
  summarizeDependencies,
} from "@/components/Dependency/presentation";
import { useDependencyChecks } from "@/components/Dependency/DependencyManager";
import { dependencyManagerPath } from "@/components/Dependency/routes";

/** A passive snapshot: opening settings never mounts installation controls or checks versions. */
export default function Dependency({
  query = "",
  onOpen,
}: {
  query?: string;
  onOpen?: () => void;
}) {
  const { t } = useTranslation();
  const contexts = useDependentComponentContextsStore((state) => state.contexts);
  const { entries } = useDependencyChecks();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const isPureClient = useIsPureClient();
  const counts = summarizeDependencies(contexts, entries);
  const matches = query
    ? contexts.filter((context) => matchesDependency(context, query, "all", t, entries[context.id]))
    : [];
  const destination = dependencyManagerPath(undefined, matches.length > 0 ? query : undefined);
  const items: SettingItem[] = [
    {
      id: "manager",
      label: (
        <div className="space-y-1">
          <span>{t("dependencies.summary")}</span>
          {matches.length > 0 && (
            <p className="text-xs text-default-400">
              {matches.map((context) => dependencyDisplayName(context, t)).join(" · ")}
            </p>
          )}
        </div>
      ),
      keywords: contexts.map((context) => dependencySearchText(context, t)),
      render: () => (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
          {contexts.length ? (
            <>
              <span>{t("dependencies.count.installed", { count: counts.installed })}</span>
              <span>{t("dependencies.count.installing", { count: counts.installing })}</span>
              <span className={counts.attention ? "text-warning-600" : "text-default-500"}>
                {t("dependencies.count.attention", { count: counts.attention })}
              </span>
            </>
          ) : (
            <span className="flex items-center gap-2 text-default-500">
              <Spinner size="sm" />
              {t("configuration.dependency.loadingComponents")}
            </span>
          )}
          <a
            href={`#${destination}`}
            onClick={onOpen}
            className="font-medium text-primary hover:underline"
          >
            {t("dependencies.manage")}
          </a>
        </div>
      ),
    },
  ];
  return (
    <SettingsSection
      title={t("configuration.dependency.title")}
      query={query}
      keywords={["dependency", "components", "依赖", "组件"]}
      items={items}
      header={
        <div className="space-y-1 text-xs text-default-500">
          <p>{t("dependencies.server", { name: serverName || t("dependencies.currentServer") })}</p>
          <p>{t("dependencies.optionalHint")}</p>
          {isPureClient && contexts.some((context) => context.id === LocaleEmulatorId) && (
            <p className="text-warning-600">
              {t("configuration.dependency.localeEmulatorRunsOnThisMachine")}
            </p>
          )}
        </div>
      }
    />
  );
}
