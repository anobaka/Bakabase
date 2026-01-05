"use client";

import type { BakabaseInsideWorldModelsModelsDtosDashboardStatistics } from "@/sdk/Api";
import type { Resource as ResourceModel } from "@/core/models/Resource";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineArrowRight,
  AiOutlineClockCircle,
  AiOutlineDatabase,
  AiOutlineEdit,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlinePushpin,
  AiOutlineRise,
  AiOutlineSearch,
  AiOutlineThunderbolt,
} from "react-icons/ai";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler,
} from "chart.js";
import { Line } from "react-chartjs-2";

import BApi from "@/sdk/BApi";
import {
  InternalProperty,
  PropertyPool,
  ResourceSearchSortableProperty,
  ResourceTag,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";
import { progressiveSearch } from "@/hooks/useResourceSearch";
import {
  Button,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
  Spinner,
} from "@/components/bakaui";
import Resource from "@/components/Resource";
import { serializeStandardValue } from "@/components/StandardValue";
import { OnboardingModal, useOnboarding } from "@/components/Onboarding";

// Register Chart.js components
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler,
);

type ResourceTab = "added" | "played" | "pinned";

const DashboardPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { showOnboarding, completeOnboarding } = useOnboarding();

  const [data, setData] = useState<BakabaseInsideWorldModelsModelsDtosDashboardStatistics>(
    {} as BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
  );
  const [property, setProperty] = useState<any>();
  const [activeTab, setActiveTab] = useState<ResourceTab>("added");
  const [recentlyAdded, setRecentlyAdded] = useState<ResourceModel[]>([]);
  const [recentlyPlayed, setRecentlyPlayed] = useState<ResourceModel[]>([]);
  const [pinnedResources, setPinnedResources] = useState<ResourceModel[]>([]);
  const [mediaLibraries, setMediaLibraries] = useState<Array<{ id: number; name: string }>>([]);
  const initializedRef = useRef(false);

  useEffect(() => {
    BApi.dashboard.getStatistics().then((res) => {
      initializedRef.current = true;
      setData(res.data || ({} as BakabaseInsideWorldModelsModelsDtosDashboardStatistics));
    });

    BApi.dashboard.getPropertyStatistics().then((r) => {
      setProperty(r.data);
    });

    // Fetch recently added with progressive loading
    progressiveSearch(
      {
        page: 1,
        pageSize: 20,
        orders: [{ property: ResourceSearchSortableProperty.AddDt, asc: false }],
      },
      setRecentlyAdded,
    );

    // Fetch recently played with progressive loading
    progressiveSearch(
      {
        page: 1,
        pageSize: 20,
        orders: [{ property: ResourceSearchSortableProperty.PlayedAt, asc: false }],
      },
      setRecentlyPlayed,
      (resources) => resources.filter((r) => r.playedAt),
    );

    // Fetch pinned resources with progressive loading
    progressiveSearch(
      {
        page: 1,
        pageSize: 20,
        tags: [ResourceTag.Pinned],
        orders: [{ property: ResourceSearchSortableProperty.AddDt, asc: false }],
      },
      setPinnedResources,
    );

    // Fetch media libraries for Browse Libraries dropdown
    BApi.mediaLibraryV2.getAllMediaLibraryV2().then((res) => {
      const libs = (res.data ?? []).map((lib) => ({
        id: lib.id!,
        name: lib.name ?? "",
      }));

      setMediaLibraries(libs);
    });
  }, []);

  // Calculate stats
  const totalResources = (data.mediaLibraryResourceCounts ?? []).reduce(
    (sum, item) => sum + item.count,
    0,
  );
  const libraryCount = (data.mediaLibraryResourceCounts ?? []).length;
  const thisWeekCount = data.resourceTrending?.find((r) => r.offset === 0)?.count ?? 0;
  const lastWeekCount = data.resourceTrending?.find((r) => r.offset === -1)?.count ?? 0;
  const weekGrowth =
    lastWeekCount > 0 ? (((thisWeekCount - lastWeekCount) / lastWeekCount) * 100).toFixed(0) : 0;
  const propertyCoverage =
    property?.totalExpectedPropertyValueCount > 0
      ? (
          (property.totalFilledPropertyValueCount / property.totalExpectedPropertyValueCount) *
          100
        ).toFixed(1)
      : 0;

  const resourceScrollRef = useRef<HTMLDivElement>(null);

  const handleWheelScroll = useCallback((e: React.WheelEvent<HTMLDivElement>) => {
    if (resourceScrollRef.current && e.deltaY !== 0) {
      e.preventDefault();
      resourceScrollRef.current.scrollLeft += e.deltaY;
    }
  }, []);

  // Get current tab resources
  const getCurrentResources = () => {
    switch (activeTab) {
      case "added":
        return recentlyAdded;
      case "played":
        return recentlyPlayed;
      case "pinned":
        return pinnedResources;
      default:
        return [];
    }
  };

  const currentResources = getCurrentResources();

  // Stat Card Component
  const StatCard = ({
    icon,
    label,
    value,
    subValue,
    color = "primary",
  }: {
    icon: React.ReactNode;
    label: string;
    value: string | number;
    subValue?: string;
    color?: "primary" | "success" | "warning" | "secondary";
  }) => {
    const colorClasses = {
      primary: "text-primary bg-primary/10",
      success: "text-success bg-success/10",
      warning: "text-warning bg-warning/10",
      secondary: "text-secondary bg-secondary/10",
    };

    return (
      <div className="flex items-center gap-4 p-5 bg-[var(--theme-block-background)] rounded-xl">
        <div className={`p-3 rounded-lg ${colorClasses[color]}`}>{icon}</div>
        <div className="flex flex-col">
          <span className="text-sm text-[var(--theme-text-subtle)]">{label}</span>
          <span className="text-2xl font-semibold">{value}</span>
          {subValue && <span className="text-xs text-[var(--theme-text-subtle)]">{subValue}</span>}
        </div>
      </div>
    );
  };

  // Mini Trend Chart
  const TrendChart = () => {
    const getCssVariable = (variableName: string) => {
      if (typeof document === "undefined") return "";

      return getComputedStyle(document.documentElement).getPropertyValue(variableName);
    };

    const chartData = data.resourceTrending?.slice().reverse() ?? [];

    if (chartData.length === 0) return null;

    const primaryColor = getCssVariable("--theme-text-primary") || "#6366f1";

    const chartConfig = {
      labels: chartData.map((_, i) => i.toString()),
      datasets: [
        {
          data: chartData.map((item) => item.count),
          borderColor: primaryColor,
          backgroundColor: `${primaryColor}20`,
          borderWidth: 2,
          fill: true,
          tension: 0.4,
          pointRadius: 0,
          pointHoverRadius: 4,
        },
      ],
    };

    const options = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: { enabled: false },
      },
      scales: {
        y: { display: false },
        x: { display: false },
      },
    };

    return (
      <div className="h-12 w-24">
        <Line data={chartConfig} options={options} />
      </div>
    );
  };

  // Tab Button Component
  const TabButton = ({
    tab,
    icon,
    label,
    count,
  }: {
    tab: ResourceTab;
    icon: React.ReactNode;
    label: string;
    count: number;
  }) => (
    <button
      className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-all ${
        activeTab === tab
          ? "bg-primary text-primary-foreground"
          : "hover:bg-[var(--theme-block-background-accent)]"
      }`}
      onClick={() => setActiveTab(tab)}
    >
      {icon}
      <span>{label}</span>
      {count > 0 && (
        <span
          className={`text-xs px-1.5 py-0.5 rounded-full ${
            activeTab === tab ? "bg-primary-foreground/20" : "bg-default-200"
          }`}
        >
          {count}
        </span>
      )}
    </button>
  );

  // Handle random play
  const handleRandomPlay = async () => {
    await BApi.resource.playRandomResource();
  };

  // Handle browse library navigation
  const handleBrowseLibrary = (libraryId: number, libraryName: string) => {
    const searchForm = {
      group: {
        combinator: 1,
        disabled: false,
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.MediaLibraryV2Multi,
            operation: SearchOperation.In,
            dbValue: serializeStandardValue([libraryId.toString()], StandardValueType.ListString),
            bizValue: serializeStandardValue([libraryName], StandardValueType.ListString),
            disabled: false,
          },
        ],
      },
      page: 1,
      pageSize: 100,
    };

    navigate(`/resource?query=${encodeURIComponent(JSON.stringify(searchForm))}`);
  };

  if (!initializedRef.current) {
    return (
      <div className="w-full h-full flex items-center justify-center">
        <Spinner size="lg" />
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col gap-5 p-5 overflow-auto">
      <OnboardingModal visible={showOnboarding} onComplete={completeOnboarding} />

      {/* Stats Row */}
      <section className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          color="primary"
          icon={<AiOutlineDatabase className="text-2xl" />}
          label={t<string>("dashboard.stat.totalResources")}
          subValue={t<string>("dashboard.stat.libraryCount", { count: libraryCount })}
          value={totalResources.toLocaleString()}
        />
        <StatCard
          color="success"
          icon={<AiOutlineFolderOpen className="text-2xl" />}
          label={t<string>("dashboard.stat.thisWeek")}
          subValue={
            Number(weekGrowth) >= 0 ? `↑ ${weekGrowth}%` : `↓ ${Math.abs(Number(weekGrowth))}%`
          }
          value={`+${thisWeekCount}`}
        />
        <div className="flex items-center gap-4 p-5 bg-[var(--theme-block-background)] rounded-xl">
          <div className="p-3 rounded-lg text-warning bg-warning/10">
            <AiOutlineRise className="text-2xl" />
          </div>
          <div className="flex flex-col flex-1">
            <span className="text-sm text-[var(--theme-text-subtle)]">{t<string>("dashboard.stat.trend")}</span>
            <div className="flex items-center justify-between">
              <span className="text-lg font-semibold">{t<string>("dashboard.stat.weeks")}</span>
              <TrendChart />
            </div>
          </div>
        </div>
        <StatCard
          color="secondary"
          icon={<div className="text-xl font-bold">{propertyCoverage}%</div>}
          label={t<string>("dashboard.stat.dataCompleteness")}
          value={`${property?.totalFilledPropertyValueCount ?? 0} / ${property?.totalExpectedPropertyValueCount ?? 0}`}
        />
      </section>

      {/* Quick Actions */}
      <section className="flex gap-3">
        <Button className="flex-1" variant="flat" onPress={() => navigate("/resource")}>
          <AiOutlineSearch className="text-lg" />
          {t<string>("dashboard.action.search")}
        </Button>
        <Dropdown>
          <DropdownTrigger>
            <Button className="flex-1" variant="flat">
              <AiOutlineFolderOpen className="text-lg" />
              {t<string>("dashboard.action.browseLibraries")}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            aria-label="Library selection"
            onAction={(key) => {
              const lib = mediaLibraries.find((l) => l.id === Number(key));

              if (lib) {
                handleBrowseLibrary(lib.id, lib.name);
              }
            }}
          >
            {mediaLibraries.map((lib) => {
              const count =
                data.mediaLibraryResourceCounts?.find((c) => c.name === lib.name)?.count ?? 0;

              return (
                <DropdownItem key={lib.id}>
                  {lib.name} ({count})
                </DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
        <Button className="flex-1" variant="flat" onPress={() => navigate("/bulk-modification")}>
          <AiOutlineEdit className="text-lg" />
          {t<string>("dashboard.action.bulkOperations")}
        </Button>
        <Button
          className="flex-1"
          color="primary"
          isDisabled={totalResources === 0}
          variant="flat"
          onPress={handleRandomPlay}
        >
          <AiOutlineThunderbolt className="text-lg" />
          {t<string>("dashboard.action.randomPick")}
        </Button>
      </section>

      {/* Resources Section with Tabs */}
      <section className="bg-[var(--theme-block-background)] rounded-xl p-5">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <TabButton
              count={recentlyAdded.length}
              icon={<AiOutlineClockCircle />}
              label={t<string>("dashboard.tab.recentlyAdded")}
              tab="added"
            />
            <TabButton
              count={recentlyPlayed.length}
              icon={<AiOutlinePlayCircle />}
              label={t<string>("dashboard.tab.recentlyPlayed")}
              tab="played"
            />
            <TabButton
              count={pinnedResources.length}
              icon={<AiOutlinePushpin />}
              label={t<string>("dashboard.tab.pinned")}
              tab="pinned"
            />
          </div>
          <Button size="sm" variant="light" onPress={() => navigate("/resource")}>
            {t<string>("dashboard.action.viewAll")}
            <AiOutlineArrowRight className="ml-1" />
          </Button>
        </div>

        {currentResources.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-4 py-8 opacity-60">
            {activeTab === "added" && <AiOutlineClockCircle className="text-5xl" />}
            {activeTab === "played" && <AiOutlinePlayCircle className="text-5xl" />}
            {activeTab === "pinned" && <AiOutlinePushpin className="text-5xl" />}
            <p>
              {activeTab === "added" && t<string>("dashboard.empty.noResources")}
              {activeTab === "played" && t<string>("dashboard.empty.noPlayHistory")}
              {activeTab === "pinned" && t<string>("dashboard.empty.noPinned")}
            </p>
            {activeTab === "added" && (
              <Button color="primary" variant="flat" onPress={() => navigate("/media-library")}>
                {t<string>("dashboard.action.addResources")}
              </Button>
            )}
          </div>
        ) : (
          <div
            ref={resourceScrollRef}
            className="flex items-start gap-3 overflow-x-auto pb-2"
            style={{
              scrollbarWidth: "thin",
              scrollbarColor: "transparent transparent",
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.scrollbarColor = "var(--theme-text-subtle) transparent";
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.scrollbarColor = "transparent transparent";
            }}
            onWheel={handleWheelScroll}
          >
            {currentResources.map((resource) => (
              <Resource key={resource.id} className="flex-shrink-0 w-36" resource={resource} />
            ))}
          </div>
        )}
      </section>

      {/* Bottom Row: Needs Attention + Library Distribution */}
      <section className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        {/* Library Distribution */}
        {(data.mediaLibraryResourceCounts ?? []).length > 0 && (
          <div className="bg-[var(--theme-block-background)] rounded-xl p-5">
            <h3 className="text-lg font-semibold mb-3">{t<string>("dashboard.section.libraryDistribution")}</h3>
            <div className="flex flex-wrap gap-2">
              {data.mediaLibraryResourceCounts?.map((lib) => (
                <div
                  key={lib.name}
                  className="flex items-center gap-2 px-3 py-2 bg-[var(--theme-block-background-accent)] rounded-lg"
                >
                  <span className="text-sm">{lib.name}</span>
                  <span className="text-sm font-semibold text-primary">{lib.count}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </section>
    </div>
  );
};

DashboardPage.displayName = "DashboardPage";

export default DashboardPage;
