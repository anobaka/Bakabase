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

const RequestResultTypeIntervalColorMap = {
  [ThirdPartyRequestResultType.Succeed]: "#46bc15",
  [ThirdPartyRequestResultType.Failed]: "#ff3000",
  [ThirdPartyRequestResultType.Banned]: "#993300",
  [ThirdPartyRequestResultType.Canceled]: "#666",
  [ThirdPartyRequestResultType.TimedOut]: "#ff9300",
};

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

  const chartData = {
    labels: thirdParties.map((id) => ThirdPartyId[id]),
    datasets: resultTypes.map((resultType) => ({
      label: resultType,
      data: thirdParties.map((id) => groupedData[id][resultType] || 0),
      backgroundColor: RequestResultTypeIntervalColorMap[resultType],
      borderColor: RequestResultTypeIntervalColorMap[resultType],
      borderWidth: 1,
    })),
  };

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        position: "top" as const,
      },
      tooltip: {
        backgroundColor: "rgba(0,0,0,0.8)",
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
          color: "rgba(0, 0, 0, 0.7)",
        },
      },
      y: {
        stacked: true,
        beginAtZero: true,
        grid: {
          color: "rgba(0, 0, 0, 0.1)",
        },
        ticks: {
          color: "rgba(0, 0, 0, 0.7)",
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
                result: ThirdPartyRequestResultType[r],
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
                  {}
                </BakauiTooltip>
              </div>
            );
          })}
        </div>
      </Button>
    </div>
  );
};
