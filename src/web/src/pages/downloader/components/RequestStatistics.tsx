"use client";

import type { components } from "@/sdk/BApi2";

import { useTranslation } from "react-i18next";
import React, { useEffect, useRef, useState } from "react";
import { AiOutlineBarChart } from "react-icons/ai";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { Bar } from "react-chartjs-2";

import { ThirdPartyId, ThirdPartyRequestResultType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  Button,
  Chip,
  Modal,
  Tooltip as BakauiTooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";

// Register Chart.js components
ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
);

type RequestStatistics =
  components["schemas"]["Bakabase.InsideWorld.Models.Models.Aos.ThirdPartyRequestStatistics"];

// Chart component using Chart.js
const BizChartsChart = ({
  data,
  height = 300,
}: {
  data: any[];
  height?: number;
}) => {
  const { t } = useTranslation();
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

  // Dynamic color mapping based on theme
  const getRequestResultTypeColorMap = () => {
    const successColor = getCssVariable("--theme-color-success") || "#46bc15";
    const dangerColor = getCssVariable("--theme-text-error") || "#ff3000";
    const warningColor = getCssVariable("--theme-color-warning") || "#ff9300";
    const subtleColor = getCssVariable("--theme-text-subtle") || "#666";
    const bannedColor = "#993300"; // Keep this fixed as it's specific

    return {
      [ThirdPartyRequestResultType.Succeed]: successColor,
      [ThirdPartyRequestResultType.Failed]: dangerColor,
      [ThirdPartyRequestResultType.Banned]: bannedColor,
      [ThirdPartyRequestResultType.Canceled]: subtleColor,
      [ThirdPartyRequestResultType.TimedOut]: warningColor,
    } as Record<ThirdPartyRequestResultType, string>;
  };

  if (!Array.isArray(data) || data.length === 0) {
    return (
      <div className="flex justify-center py-4 text-gray-500">
        No data available
      </div>
    );
  }

  // Group data by third party and result type
  const groupedData = data.reduce(
    (acc, item) => {
      if (!acc[item.id]) {
        acc[item.id] = {};
      }
      acc[item.id][item.result] = item.count;

      return acc;
    },
    {} as Record<string, Record<string, number>>,
  );

  const thirdParties = Object.keys(groupedData);
  const resultTypes = Object.values(ThirdPartyRequestResultType);
  const colorMap = getRequestResultTypeColorMap();

  const chartData = {
    labels: thirdParties.map(
      (id) => ThirdPartyId[id as keyof typeof ThirdPartyId],
    ),
    datasets: resultTypes.map((resultType) => ({
      label: t(ThirdPartyRequestResultType[resultType]),
      data: thirdParties.map((id) => groupedData[id][resultType] || 0),
      backgroundColor: colorMap[resultType as ThirdPartyRequestResultType],
      borderColor: colorMap[resultType as ThirdPartyRequestResultType],
      borderWidth: 1,
    })),
  };

  // Get theme colors for chart styling
  const textColor =
    getCssVariable("--theme-text") || (isDarkMode ? "#e6e6e6" : "#000");
  const subtleColor =
    getCssVariable("--theme-text-subtle") || (isDarkMode ? "#c0c0c0" : "#666");
  const borderColor =
    getCssVariable("--theme-border-color") ||
    (isDarkMode ? "rgb(48, 54, 61)" : "#f0f0f0");
  const tooltipBg = isDarkMode ? "rgba(0,0,0,0.9)" : "rgba(0,0,0,0.8)";

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: "top" as const,
        labels: {
          color: textColor,
        },
      },
      tooltip: {
        backgroundColor: tooltipBg,
        titleColor: "#fff",
        bodyColor: "#fff",
        borderColor: "rgba(0, 0, 0, 0.2)",
        borderWidth: 1,
      },
    },
    scales: {
      x: {
        stacked: true,
        grid: {
          display: false,
        },
        ticks: {
          color: subtleColor,
        },
      },
      y: {
        stacked: true,
        beginAtZero: true,
        grid: {
          color: borderColor,
        },
        ticks: {
          color: subtleColor,
        },
      },
    },
  };

  return (
    <div style={{ height }}>
      <Bar data={chartData} options={options} />
    </div>
  );
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [requestStatistics, setRequestStatistics] = useState<
    RequestStatistics[]
  >([]);
  const gettingRequestStatistics = useRef(false);
  const requestStatisticsRef = useRef(requestStatistics);

  useEffect(() => {
    const getRequestStatisticsInterval = setInterval(() => {
      if (!gettingRequestStatistics.current) {
        gettingRequestStatistics.current = true;
        BApi.thirdParty
          .getAllThirdPartyRequestStatistics()
          .then((a) => {
            if (
              JSON.stringify(a.data) !=
              JSON.stringify(requestStatisticsRef.current)
            ) {
              setRequestStatistics(a.data ?? []);
            }
          })
          .finally(() => {
            gettingRequestStatistics.current = false;
          });
      }
    }, 1000);

    return () => {
      clearInterval(getRequestStatisticsInterval);
    };
  }, []);

  return (
    <div className="flex items-center gap-1">
      <Button
        size={"sm"}
        variant={"light"}
        onPress={() => {
          const thirdPartyRequestCounts = (requestStatistics || []).reduce<
            any[]
          >((s, t) => {
            Object.keys(t.counts || {}).forEach((r) => {
              s.push({
                id: t.id.toString(),
                name: ThirdPartyId[t.id],
                result:
                  ThirdPartyRequestResultType[
                    r as keyof typeof ThirdPartyRequestResultType
                  ],
                count: t.counts?.[r],
              });
            });

            return s;
          }, []);

          createPortal(Modal, {
            size: "xl",
            defaultVisible: true,
            children: <BizChartsChart data={thirdPartyRequestCounts} />,
            footer: {
              actions: ["ok"],
            },
            title: t<string>("Requests overview"),
          });
        }}
      >
        <div className="flex items-center gap-1">
          <AiOutlineBarChart className={"text-base"} />
          {t<string>("Requests overview")}
          {requestStatistics?.map((rs) => {
            let successCount = 0;
            let failureCount = 0;

            Object.keys(rs.counts || {}).forEach((r) => {
              const rt = parseInt(r, 10) as ThirdPartyRequestResultType;

              switch (rt) {
                case ThirdPartyRequestResultType.Succeed:
                  successCount += rs.counts![r]!;
                  break;
                default:
                  failureCount += rs.counts![r]!;
                  break;
              }
            });

            return (
              <div className="flex items-center">
                <ThirdPartyIcon size={"sm"} thirdPartyId={rs.id} />
                <BakauiTooltip content={t<string>("Success")}>
                  <Chip
                    className={"p-0"}
                    color={"success"}
                    size={"sm"}
                    variant={"light"}
                  >
                    {successCount}
                  </Chip>
                </BakauiTooltip>
                /
                <BakauiTooltip content={t<string>("Failure")}>
                  <Chip
                    className={"p-0"}
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                  >
                    {failureCount}
                  </Chip>
                </BakauiTooltip>
              </div>
            );
          })}
        </div>
      </Button>
    </div>
  );
};
