"use client";

import React, { useEffect, useRef, useState } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineArrowRight, AiOutlinePlusCircle } from "react-icons/ai";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { Line } from "react-chartjs-2";
import { Bar } from "react-chartjs-2";

import BApi from "@/sdk/BApi";

import type { BakabaseInsideWorldModelsModelsDtosDashboardStatistics } from "@/sdk/Api";

import {
  downloadTaskStatuses,
  PropertyPool,
  ThirdPartyId,
} from "@/sdk/constants";
import { Button, Chip, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

// Register Chart.js components
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
);
const DashboardPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [data, setData] =
    useState<BakabaseInsideWorldModelsModelsDtosDashboardStatistics>(
      {} as BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
    );
  const [property, setProperty] = useState<any>();
  const initializedRef = useRef(false);

  useEffect(() => {
    BApi.dashboard.getStatistics().then((res) => {
      initializedRef.current = true;
      setData(
        res.data ||
          ({} as BakabaseInsideWorldModelsModelsDtosDashboardStatistics),
      );
    });

    BApi.dashboard.getPropertyStatistics().then((r) => {
      setProperty(r.data);
    });
  }, []);

  const renderTrending = () => {
    if (data && data.resourceTrending) {
      const chartData =
        data.resourceTrending.map((r) => ({
          week:
            r.offset == 0
              ? t<string>("This week")
              : r.offset == -1
                ? t<string>("Last week")
                : `${t<string>("{{count}} weeks ago", { count: -r.offset! })}`,
          count: r.count,
        })) ?? [];

      // console.log(chartData);
      return <ChartComponent chartData={chartData} />;
    }

    return null;
  };

  const renderResourceCounts = () => {
    const list = data.mediaLibraryResourceCounts ?? [];

    if (list.length == 0) {
      return (
        <Button
          color="primary"
          variant="flat"
          onPress={() => navigate("/media-library")}
        >
          <AiOutlinePlusCircle className="text-base" />
          {t<string>("Add your resources")}
        </Button>
      );
    }

    // 统一主题色获取
    const getCssVariable = (variableName: string) => {
      if (typeof document === "undefined") return "";

      return getComputedStyle(document.documentElement).getPropertyValue(
        variableName,
      );
    };
    const primaryColor =
      getCssVariable("--theme-text-primary") || "rgba(59,130,246,1)";
    const textColor = getCssVariable("--theme-text") || "#222";
    const subtleColor = getCssVariable("--theme-text-subtle") || "#888";
    const borderColor = getCssVariable("--theme-border-color") || "#eee";
    const blockBg = getCssVariable("--theme-block-background") || "#fff";

    // 统一 options
    const options = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: blockBg,
          titleColor: textColor,
          bodyColor: textColor,
          borderColor: primaryColor,
          borderWidth: 1,
        },
      },
      scales: {
        y: {
          beginAtZero: true,
          grid: { color: borderColor },
          ticks: { color: subtleColor },
        },
        x: {
          grid: { display: false },
          ticks: { color: subtleColor },
        },
      },
    };

    const chartData = {
      labels: list.map((item) => item.name),
      datasets: [
        {
          label: t<string>("Resource count"),
          data: list.map((item) => item.count),
          backgroundColor: `${primaryColor}33`,
          borderColor: primaryColor,
          borderWidth: 2,
          borderRadius: 8, // 圆角
          maxBarThickness: 32, // 柱子最大宽度
        },
      ],
    };

    const total = (data.mediaLibraryResourceCounts ?? []).reduce(
      (s, t) => s + t.count,
      0,
    );

    return (
      <div className="w-full h-44 flex gap-2 items-start">
        <div>
          <div className={"text-lg"}>{t<string>("Count of resources")}</div>
          <div className={"text-3xl"}>{total}</div>
        </div>
        <div className={"grow"}>
          <Bar data={chartData} options={options} />
        </div>
      </div>
    );
  };

  return (
    <div className={"dashboard-page"}>
      {initializedRef.current ? (
        <>
          <section className={"h-1/3 max-h-1/3"}>
            <div className="block w-2/3">
              <div className={"title"}>{t<string>("Overview")}</div>
              <div className={"content min-h-0"}>{renderResourceCounts()}</div>
            </div>
            <div className="block trending" style={{ flex: 1 }}>
              <div className="title">{t<string>("Trending")}</div>
              <div className="content">{renderTrending()}</div>
            </div>
          </section>
          <section style={{ maxHeight: "40%" }}>
            <div className="block" style={{ flex: 2.5 }}>
              <div className={"title flex items-center gap-2"}>
                {t<string>("Property value coverage")}
                <div className={"text-sm opacity-60"}>
                  {t<string>(
                    "As more data is filled in, property value coverage increases",
                  )}
                </div>
              </div>
              {property ? (
                <div className={"flex items-start gap-8 min-h-0"}>
                  <div className={"w-[200px]"}>
                    <div className={"text-lg"}>{t<string>("Overall")}</div>
                    <div className={"text-3xl"}>
                      {property.totalExpectedPropertyValueCount > 0
                        ? (
                            (property.totalFilledPropertyValueCount /
                              property.totalExpectedPropertyValueCount) *
                            100
                          ).toFixed(2)
                        : 0}
                      %
                    </div>
                    <div className={"opacity-60 text-xs"}>
                      {property.totalExpectedPropertyValueCount > 0
                        ? property.totalFilledPropertyValueCount /
                          property.totalExpectedPropertyValueCount
                        : 0}
                    </div>
                  </div>
                  <div className={"flex flex-col min-h-0 max-h-full"}>
                    <div className={"text-lg"}>{t<string>("Details")}</div>
                    <div
                      className={"flex flex-wrap gap-2 min-h-0 overflow-auto"}
                    >
                      {property.propertyValueCoverages?.map((x: any) => {
                        return (
                          <div>
                            <div className={"flex items-center gap-1"}>
                              <Chip
                                color={
                                  x.pool == PropertyPool.Reserved
                                    ? "secondary"
                                    : "success"
                                }
                                radius={"sm"}
                                size={"sm"}
                                variant={"flat"}
                              >
                                {x.name}
                              </Chip>
                              <div>
                                {(
                                  (x.filledCount / x.expectedCount) *
                                  100
                                ).toFixed(2)}
                                %
                              </div>
                            </div>
                            <div className={"opacity-60 text-xs text-center"}>
                              {x.filledCount} / {x.expectedCount}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                </div>
              ) : (
                <div className={"flex justify-center py-4"}>
                  <Spinner />
                </div>
              )}
            </div>
          </section>
          <section>
            <div className="block" style={{ flex: 1.5 }}>
              <div className={"title"}>{t<string>("Downloader")}</div>
              <div className="content">
                {data.downloaderDataCounts &&
                data.downloaderDataCounts.length > 0 ? (
                  <>
                    <div className={"downloader-item"}>
                      <div>{t<string>("Third party")}</div>
                      {downloadTaskStatuses.map((s, i) => {
                        return <div key={i}>{t<string>(s.label)}</div>;
                      })}
                    </div>
                    {data.downloaderDataCounts?.map((c) => {
                      return (
                        <div className={"downloader-item"}>
                          <div>{t<string>(ThirdPartyId[c.id]!)}</div>
                          {downloadTaskStatuses.map((s, i) => {
                            return (
                              <div key={i}>
                                {c.statusAndCounts?.[s.value] ?? 0}
                              </div>
                            );
                          })}
                        </div>
                      );
                    })}
                  </>
                ) : (
                  t<string>("No content")
                )}
              </div>
            </div>
            <div className="blocks">
              {data.otherCounts?.map((list, r) => {
                return (
                  <section key={r}>
                    {list.map((c, j) => {
                      return (
                        <div key={j} className="block">
                          <div className="content">
                            <div key={j} className="flex items-center gap-1">
                              <Chip radius={"sm"} size={"sm"}>
                                {t<string>(c.name)}
                              </Chip>
                              {c.count}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </section>
                );
              })}
              <section>
                <div className="block">
                  <div className="content">
                    <div className="t-t-c file-mover">
                      <div className="left">
                        <div className="text">{t<string>("File mover")}</div>
                      </div>
                      <div className="right">
                        <div className="count flex items-center gap-1">
                          {data.fileMover?.sourceCount ?? 0}
                          <AiOutlineArrowRight />
                          {data.fileMover?.targetCount ?? 0}
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </section>
            </div>
            <div className="block hidden" style={{ flex: 1.5 }} />
          </section>
        </>
      ) : (
        <div className={"w-full h-full flex items-center justify-center"}>
          <Spinner size={"lg"} />
        </div>
      )}
    </div>
  );
};

DashboardPage.displayName = "DashboardPage";

// Chart component using Chart.js
const ChartComponent = ({ chartData }: { chartData: any }) => {
  const { isDarkMode } = useBakabaseContext();

  // Get CSS variables for theme colors
  const getCssVariable = (variableName: string) => {
    // Check if we're in browser environment
    if (typeof document === "undefined") {
      return "";
    }

    return getComputedStyle(document.documentElement).getPropertyValue(
      variableName,
    );
  };

  // Ensure chartData is an array and has data
  if (!Array.isArray(chartData) || chartData.length === 0) {
    return (
      <div className="flex justify-center py-4 text-gray-500">
        No data available
      </div>
    );
  }

  const primaryColor = getCssVariable("--theme-text-primary");
  const textColor = getCssVariable("--theme-text");
  const subtleColor = getCssVariable("--theme-text-subtle");
  const borderColor = getCssVariable("--theme-border-color");
  const blockBg = getCssVariable("--theme-block-background");

  const data = {
    labels: chartData.map((item) => item.week),
    datasets: [
      {
        label: "Count",
        data: chartData.map((item) => item.count),
        borderColor: primaryColor,
        backgroundColor: `${primaryColor}33`, // 20ity
        borderWidth: 2,
        fill: true,
        tension: 0.4,
        pointBackgroundColor: primaryColor,
        pointBorderColor: textColor,
        pointBorderWidth: 2,
        pointRadius: 4,
        pointHoverRadius: 6,
      },
    ],
  };

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        display: false,
      },
      tooltip: {
        backgroundColor: blockBg,
        titleColor: textColor,
        bodyColor: textColor,
        borderColor: primaryColor,
        borderWidth: 1,
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        grid: {
          color: borderColor,
        },
        ticks: {
          color: subtleColor,
        },
      },
      x: {
        display: false, // 隐藏X轴标签
        grid: {
          display: false,
        },
      },
    },
  };

  return (
    <div className="w-full h-44">
      <Line data={data} options={options} />
    </div>
  );
};

export default DashboardPage;
