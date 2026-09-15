"use client";

import type { RecentResourceTab } from "./recentResourcesData";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineArrowRight,
  AiOutlineClockCircle,
  AiOutlinePlayCircle,
  AiOutlinePushpin,
  AiOutlineReload,
} from "react-icons/ai";

import { buildRecentResourceSearch } from "./recentResourcesData";
import { useRecentResources } from "./useRecentResources";

import { Button, Spinner, Tab, Tabs } from "@/components/bakaui";
import Resource from "@/components/Resource";
import { usePendingSearchStore } from "@/stores/pendingSearch";

const tabs = [
  {
    key: "added",
    label: "dashboard.tab.recentlyAdded",
    empty: "dashboard.empty.noResources",
    icon: AiOutlineClockCircle,
  },
  {
    key: "played",
    label: "dashboard.tab.recentlyPlayed",
    empty: "dashboard.empty.noPlayHistory",
    icon: AiOutlinePlayCircle,
  },
  {
    key: "pinned",
    label: "dashboard.tab.pinned",
    empty: "dashboard.empty.noPinned",
    icon: AiOutlinePushpin,
  },
] as const;

export default function RecentResources({ refreshKey }: { refreshKey: number }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<RecentResourceTab>("added");
  const { resources, totalCount, status, retry, removeResources } = useRecentResources(
    activeTab,
    refreshKey,
  );
  const setPendingSearch = usePendingSearchStore((store) => store.setPendingSearch);

  const viewAll = () => {
    // Keep the full page's usual batch size rather than carrying the dashboard's 12-card limit.
    setPendingSearch(buildRecentResourceSearch(activeTab, 100));
    navigate("/resource");
  };

  return (
    <section
      aria-labelledby="dashboard-recent-resources"
      className="min-w-0 rounded-2xl bg-content1 p-4 sm:p-5"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold" id="dashboard-recent-resources">
          {t<string>("dashboard.resources.title")}
        </h2>
        <Button
          endContent={<AiOutlineArrowRight className="text-base" />}
          size="sm"
          variant="light"
          onPress={viewAll}
        >
          {t<string>("dashboard.action.viewAll")}
        </Button>
      </div>
      <Tabs
        aria-label={t<string>("dashboard.resources.tabsLabel")}
        classNames={{ base: "max-w-full", tabList: "max-w-full", panel: "px-0 pb-0 pt-4" }}
        color="primary"
        selectedKey={activeTab}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setActiveTab(key as RecentResourceTab)}
      >
        {tabs.map(({ key, label, empty, icon: Icon }) => (
          <Tab
            key={key}
            title={
              <span className="flex items-center gap-1.5">
                <Icon className="hidden text-base sm:block" />
                {t<string>(label)}
              </span>
            }
          >
            {status === "loading" ? (
              <div
                aria-live="polite"
                className="flex min-h-48 items-center justify-center"
                role="status"
              >
                <Spinner label={t<string>("dashboard.resources.loading")} size="sm" />
              </div>
            ) : status === "error" ? (
              <div
                className="flex min-h-48 flex-col items-center justify-center gap-3"
                role="alert"
              >
                <p className="text-sm text-default-500">
                  {t<string>("dashboard.resources.loadFailed")}
                </p>
                <Button
                  size="sm"
                  startContent={<AiOutlineReload className="text-base" />}
                  variant="flat"
                  onPress={retry}
                >
                  {t<string>("dashboard.resources.retry")}
                </Button>
              </div>
            ) : resources.length === 0 ? (
              <div className="flex min-h-48 flex-col items-center justify-center gap-3 text-default-500">
                <Icon aria-hidden className="text-3xl" />
                <p className="text-sm">{t<string>(empty)}</p>
                {key === "added" && (
                  <Button
                    color="primary"
                    size="sm"
                    variant="flat"
                    onPress={() => navigate("/path-mark-config")}
                  >
                    {t<string>("dashboard.action.addResources")}
                  </Button>
                )}
              </div>
            ) : (
              <>
                <div className="grid grid-cols-2 items-start gap-3 sm:grid-cols-[repeat(auto-fill,minmax(min(100%,8rem),1fr))]">
                  {resources.map((resource) => (
                    <Resource
                      key={resource.id}
                      className="min-w-0 w-full"
                      resource={resource}
                      onResourcesDeleted={removeResources}
                    />
                  ))}
                </div>
                <p className="mt-3 text-xs text-default-400">
                  {t<string>("dashboard.resources.showing", {
                    shown: resources.length,
                    total: totalCount,
                  })}
                </p>
              </>
            )}
          </Tab>
        ))}
      </Tabs>
    </section>
  );
}
