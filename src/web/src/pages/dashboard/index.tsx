"use client";

import type { ReactNode } from "react";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineArrowRight,
  AiOutlineCloudDownload,
  AiOutlineDatabase,
  AiOutlineEdit,
  AiOutlineFolderOpen,
  AiOutlineRead,
  AiOutlineReload,
  AiOutlineSearch,
} from "react-icons/ai";

import { DataMigrationHintModal } from "./components/DataMigrationHintModal";
import RecentResources from "./components/RecentResources";
import ActivityOverview from "./components/ActivityOverview";
import { useDashboardOverview } from "./hooks/useDashboardOverview";
import { dashboardResourceSearch } from "./dashboardSearch";

import { Button, Card, CardBody, Input, Tooltip } from "@/components/bakaui";
import {
  GETTING_STARTED_FIRST_RUN_KEY,
  HelpCenterModal,
  useFirstRunHelp,
} from "@/components/HelpCenter";
import { usePendingSearchStore } from "@/stores/pendingSearch";

function SummaryCard({
  label,
  value,
  description,
  icon,
  onPress,
  tone = "text-primary bg-primary/10",
}: {
  label: string;
  value?: number;
  description: string;
  icon: ReactNode;
  onPress: () => void;
  tone?: string;
}) {
  return (
    <Card
      isPressable
      className="w-full min-w-0 border border-default-200/50 bg-content1 text-left"
      shadow="none"
      onPress={onPress}
    >
      <CardBody className="gap-3 p-4 sm:p-5">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm text-default-500">{label}</span>
          <span aria-hidden className={`hidden rounded-lg p-2 text-lg sm:inline-flex ${tone}`}>
            {icon}
          </span>
        </div>
        <div className="text-3xl font-semibold tabular-nums tracking-tight">
          {value === undefined ? "—" : value.toLocaleString()}
        </div>
        <div className="flex items-center justify-between gap-2 text-xs text-default-500">
          <span>{description}</span>
          <AiOutlineArrowRight aria-hidden className="shrink-0" />
        </div>
      </CardBody>
    </Card>
  );
}

const shortcuts = [
  {
    path: "/path-mark-config",
    icon: AiOutlineFolderOpen,
    title: "dashboard.shortcuts.local.title",
    description: "dashboard.shortcuts.local.description",
  },
  {
    path: "/file-processor",
    icon: AiOutlineEdit,
    title: "dashboard.shortcuts.organize.title",
    description: "dashboard.shortcuts.organize.description",
  },
  {
    path: "/post-parser",
    icon: AiOutlineRead,
    title: "dashboard.shortcuts.parse.title",
    description: "dashboard.shortcuts.parse.description",
  },
];

export default function DashboardPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { showFirstRun, completeFirstRun, deferRest } = useFirstRunHelp(
    GETTING_STARTED_FIRST_RUN_KEY,
    "gettingStarted",
  );
  /**
   * A link out of the welcome: the reader went to a page, so what waits after the welcome
   * (notices, release notes) waits for the next launch instead of opening over it.
   */
  const leaveWelcomeFor = (path: string) => {
    deferRest();
    completeFirstRun();
    navigate(path);
  };
  const [refreshKey, setRefreshKey] = useState(0);
  const [keyword, setKeyword] = useState("");
  const { data, loading, error, updatedAt } = useDashboardOverview(refreshKey);
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);
  const refresh = () => setRefreshKey((value) => value + 1);
  const browse = (options: Parameters<typeof dashboardResourceSearch>[0] = {}) => {
    setPendingSearch(dashboardResourceSearch(options));
    navigate("/resource");
  };

  return (
    <div className="mx-auto flex w-full max-w-[1600px] flex-col gap-5 p-4 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{t<string>("dashboard.title")}</h1>
          <p className="mt-1 text-sm text-default-500">{t<string>("dashboard.description")}</p>
        </div>
        <form
          className="flex w-full items-center gap-2 sm:w-auto"
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            browse({ keyword });
          }}
        >
          <Input
            aria-label={t<string>("dashboard.search.label")}
            className="min-w-0 flex-1 sm:w-64"
            classNames={{ inputWrapper: "bg-content1 shadow-none border border-default-200" }}
            placeholder={t<string>("dashboard.search.placeholder")}
            size="md"
            value={keyword}
            onValueChange={setKeyword}
          />
          <Button
            isIconOnly
            aria-label={t<string>("dashboard.action.search")}
            color="primary"
            type="submit"
          >
            <AiOutlineSearch className="text-xl" />
          </Button>
          <Tooltip content={t<string>("dashboard.action.refresh")}>
            <Button
              isIconOnly
              aria-label={t<string>("dashboard.action.refresh")}
              isLoading={loading}
              variant="flat"
              onPress={refresh}
            >
              <AiOutlineReload className="text-xl" />
            </Button>
          </Tooltip>
        </form>
      </header>

      <section
        aria-busy={loading}
        aria-label={t<string>("dashboard.overview.title")}
        className="flex flex-col gap-3"
      >
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
          <span>
            {data
              ? t<string>("dashboard.stat.addedThisWeek", { count: data.thisWeekAddedCount })
              : t<string>("dashboard.overview.title")}
          </span>
          {updatedAt && (
            <span>
              {t<string>("dashboard.updatedAt", {
                time: updatedAt.toLocaleTimeString(i18n.language, {
                  hour: "2-digit",
                  minute: "2-digit",
                }),
              })}
            </span>
          )}
        </div>
        {error && (
          <div
            className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-warning/10 px-4 py-3 text-sm text-warning-700"
            role="alert"
          >
            <span>{t<string>(data ? "dashboard.error.stale" : "dashboard.error.load")}</span>
            <Button size="sm" variant="light" onPress={refresh}>
              {t<string>("dashboard.action.retry")}
            </Button>
          </div>
        )}
        <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
          <SummaryCard
            description={t<string>("dashboard.stat.totalDescription")}
            icon={<AiOutlineDatabase />}
            label={t<string>("dashboard.stat.totalResources")}
            value={data?.totalResourceCount}
            onPress={() => browse()}
          />
          <SummaryCard
            description={t<string>("dashboard.stat.localDescription")}
            icon={<AiOutlineFolderOpen />}
            label={t<string>("dashboard.stat.localResources")}
            tone="text-success bg-success/10"
            value={data?.localResourceCount}
            onPress={() => browse({ localOnly: true })}
          />
          <SummaryCard
            description={t<string>("dashboard.stat.pendingDescription")}
            icon={<AiOutlineCloudDownload />}
            label={t<string>("dashboard.stat.pendingResources")}
            tone="text-warning bg-warning/10"
            value={data?.pendingResourceCount}
            onPress={() => navigate("/acquisitions")}
          />
          <SummaryCard
            description={t<string>("dashboard.stat.collectionsDescription")}
            icon={<AiOutlineRead />}
            label={t<string>("dashboard.stat.collections")}
            tone="text-secondary bg-secondary/10"
            value={data?.collectionCount}
            onPress={() => navigate("/collections")}
          />
        </div>
      </section>

      <nav
        aria-label={t<string>("dashboard.shortcuts.title")}
        className="grid gap-2 sm:grid-cols-3"
      >
        {shortcuts.map(({ path, title, description, icon: Icon }) => (
          <Card
            key={path}
            isPressable
            className="w-full bg-content1 text-left"
            shadow="none"
            onPress={() => navigate(path)}
          >
            <CardBody className="flex-row items-center gap-3 px-4 py-3">
              <Icon aria-hidden className="shrink-0 text-xl text-default-500" />
              <div className="min-w-0 flex-1">
                <div className="text-sm font-medium">{t<string>(title)}</div>
                <div className="mt-0.5 text-xs text-default-500">{t<string>(description)}</div>
              </div>
              <AiOutlineArrowRight aria-hidden className="shrink-0 text-default-400" />
            </CardBody>
          </Card>
        ))}
      </nav>

      <RecentResources refreshKey={refreshKey} />
      {data && <ActivityOverview workflows={data.workflows} />}

      {data && (
        <section
          aria-labelledby="dashboard-libraries"
          className="rounded-2xl bg-content1 p-4 sm:p-5"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <h2 className="text-base font-semibold" id="dashboard-libraries">
                {t<string>("dashboard.libraries.title")}
              </h2>
              <span className="text-sm tabular-nums text-default-500">
                {data.mediaLibraryCount}
              </span>
            </div>
            <Button
              endContent={<AiOutlineArrowRight className="text-base" />}
              size="sm"
              variant="light"
              onPress={() => navigate("/media-library")}
            >
              {t<string>("dashboard.libraries.manage")}
            </Button>
          </div>
          {data.mediaLibraries.length ? (
            <>
              <p className="mt-1 text-xs text-default-500">
                {t<string>("dashboard.libraries.description")}
              </p>
              <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {data.mediaLibraries.map((library) => (
                  <Card
                    key={library.id}
                    isPressable
                    className="w-full bg-default-100/60 text-left"
                    shadow="none"
                    onPress={() => browse({ libraryId: library.id })}
                  >
                    <CardBody className="flex-row items-center gap-3 p-3">
                      <AiOutlineFolderOpen
                        aria-hidden
                        className="shrink-0 text-lg text-default-500"
                      />
                      <span className="min-w-0 flex-1 truncate text-sm">
                        {library.name || t<string>("dashboard.libraries.unnamed")}
                      </span>
                      <span className="text-sm tabular-nums text-default-500">
                        {library.resourceCount.toLocaleString()}
                      </span>
                      <AiOutlineArrowRight aria-hidden className="shrink-0 text-default-400" />
                    </CardBody>
                  </Card>
                ))}
              </div>
            </>
          ) : (
            <div className="flex flex-wrap items-center justify-between gap-3 py-4">
              <p className="max-w-lg text-sm text-default-500">
                {t<string>("dashboard.libraries.empty")}
              </p>
              <Button
                color="primary"
                size="sm"
                startContent={<AiOutlineFolderOpen />}
                variant="flat"
                onPress={() => navigate("/media-library")}
              >
                {t<string>("dashboard.libraries.create")}
              </Button>
            </div>
          )}
        </section>
      )}
      <HelpCenterModal
        firstRun
        topic="gettingStarted"
        visible={showFirstRun}
        onClose={completeFirstRun}
        onNavigate={leaveWelcomeFor}
      />
      <DataMigrationHintModal />
    </div>
  );
}
