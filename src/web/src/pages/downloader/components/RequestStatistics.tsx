"use client";

import type { components } from "@/sdk/BApi2";
import type { TooltipItem } from "chart.js";

import { useTranslation } from "react-i18next";
import React from "react";
import { AiOutlineBarChart } from "react-icons/ai";
import { Chart as ChartJS, ArcElement, Tooltip, Legend } from "chart.js";
import { Doughnut } from "react-chartjs-2";

import { ThirdPartyId, ThirdPartyRequestResultType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Chip, Modal, Tooltip as BakauiTooltip } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { useThirdPartyRequestStatisticsStore } from "@/stores/thirdPartyRequestStatistics";
import { humanFileSize } from "@/components/utils";

ChartJS.register(ArcElement, Tooltip, Legend);

type RequestStatistics =
  components["schemas"]["Bakabase.InsideWorld.Models.Models.Aos.ThirdPartyRequestStatistics"];
const RequestStatistics = ({ compact = false }: { compact?: boolean }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const requestStatistics = useThirdPartyRequestStatisticsStore((state) => state.statistics);

  return (
    <div className="flex items-center gap-1">
      <Button
        aria-label={t("downloader.label.requestsOverview")}
        isIconOnly={compact}
        size={"sm"}
        title={t("downloader.label.requestsOverview")}
        variant={"light"}
        onPress={() => {
          createPortal(Modal, {
            size: "xl",
            defaultVisible: true,
            children: <StatisticsModalContents />,
            footer: {
              actions: ["ok"],
            },
            title: t<string>("downloader.label.requestsOverview"),
          });
        }}
      >
        <div className="flex items-center gap-1">
          <AiOutlineBarChart className={"text-base"} />
          <span className={compact ? "sr-only" : undefined}>
            {t<string>("downloader.label.requestsOverview")}
          </span>
          {!compact &&
            requestStatistics?.map((rs) => {
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
                <div key={rs.id} className="flex items-center">
                  <ThirdPartyIcon size={"sm"} thirdPartyId={rs.id} />
                  <BakauiTooltip content={t<string>("downloader.label.success")}>
                    <Chip className={"p-0"} color={"success"} size={"sm"} variant={"light"}>
                      {successCount}
                    </Chip>
                  </BakauiTooltip>
                  /
                  <BakauiTooltip content={t<string>("downloader.label.failure")}>
                    <Chip className={"p-0"} color={"danger"} size={"sm"} variant={"light"}>
                      {failureCount}
                    </Chip>
                  </BakauiTooltip>
                  <BakauiTooltip content={t<string>("downloader.label.responseTraffic")}>
                    <Chip className={"p-0"} size={"sm"} variant={"light"}>
                      {humanFileSize(rs.receivedBytes ?? 0)}
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

RequestStatistics.displayName = "RequestStatistics";

const StatisticsModalContents = () => {
  const { t } = useTranslation();
  const statistics = useThirdPartyRequestStatisticsStore((state) => state.statistics);

  if (statistics.length === 0) {
    return (
      <div className="flex justify-center py-4 text-foreground-500">{t("common.state.noData")}</div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <section aria-label={t<string>("downloader.label.requestCounts")}>
        <h3 className="mb-3 font-semibold">{t("downloader.label.requestCounts")}</h3>
        <RequestCountsChart data={statistics} />
      </section>
      <section aria-label={t<string>("downloader.label.responseTraffic")}>
        <h3 className="mb-1 font-semibold">{t("downloader.label.responseTraffic")}</h3>
        <p className="mb-2 text-xs text-foreground-500">{t("downloader.tip.responseTraffic")}</p>
        <TrafficChart data={statistics} />
      </section>
    </div>
  );
};

const requestResultTypes = Object.values(ThirdPartyRequestResultType).filter(
  (value): value is ThirdPartyRequestResultType => typeof value === "number",
);

const requestResultColors: Record<ThirdPartyRequestResultType, string> = {
  [ThirdPartyRequestResultType.Succeed]: "var(--theme-color-success, #46bc15)",
  [ThirdPartyRequestResultType.Failed]: "var(--theme-text-error, #ff3000)",
  [ThirdPartyRequestResultType.Banned]: "#993300",
  [ThirdPartyRequestResultType.Canceled]: "var(--theme-text-subtle, #888)",
  [ThirdPartyRequestResultType.TimedOut]: "var(--theme-color-warning, #ff9300)",
};

const trafficColors = [
  "#4285f4",
  "#9b6cdb",
  "#16a6a1",
  "#f0a43b",
  "#ea6b73",
  "#62a640",
  "#5c7ce0",
  "#d56aa8",
  "#47a8ce",
  "#ae8d53",
  "#7f8e9d",
  "#9dba43",
  "#df8560",
];

const getSourceName = (source: RequestStatistics) => ThirdPartyId[source.id] ?? String(source.id);
const getTrafficColor = (source: RequestStatistics) =>
  trafficColors[(source.id - 1 + trafficColors.length) % trafficColors.length];

const RequestCountsChart = ({ data }: { data: RequestStatistics[] }) => {
  const { t } = useTranslation();
  const sources = [...data].sort((a, b) => {
    const total = (source: RequestStatistics) =>
      Object.values(source.counts || {}).reduce((sum, count) => sum + (count ?? 0), 0);

    return total(b) - total(a);
  });
  const totals = sources.map((source) =>
    Object.values(source.counts || {}).reduce((sum, count) => sum + (count ?? 0), 0),
  );
  const maxTotal = Math.max(1, ...totals);
  const visibleResultTypes = requestResultTypes.filter((result) =>
    sources.some((source) => (source.counts?.[result] ?? 0) > 0),
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-foreground-500">
        {visibleResultTypes.map((result) => (
          <span key={result} className="flex items-center gap-1">
            <span
              aria-hidden="true"
              className="size-2 rounded-full"
              style={{ backgroundColor: requestResultColors[result] }}
            />
            {t(ThirdPartyRequestResultType[result])}
          </span>
        ))}
      </div>
      <div className="flex flex-col gap-3">
        {sources.map((source, index) => {
          const total = totals[index];

          return (
            <div
              key={source.id}
              className="grid grid-cols-[minmax(5rem,7rem)_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 text-sm"
              data-testid={`request-source-${source.id}`}
            >
              <span className="truncate" title={getSourceName(source)}>
                {getSourceName(source)}
              </span>
              <div aria-hidden="true" className="h-3 overflow-hidden rounded-full bg-default-100">
                <div className="flex h-full" style={{ width: `${(total / maxTotal) * 100}%` }}>
                  {requestResultTypes.map((result) => {
                    const count = source.counts?.[result] ?? 0;

                    return count > 0 ? (
                      <span
                        key={result}
                        style={{
                          width: `${(count / total) * 100}%`,
                          backgroundColor: requestResultColors[result],
                        }}
                      />
                    ) : null;
                  })}
                </div>
              </div>
              <strong className="min-w-8 text-right tabular-nums">{total.toLocaleString()}</strong>
              {total > 0 && (
                <div className="col-start-2 col-span-2 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-foreground-500">
                  {requestResultTypes.map((result) => {
                    const count = source.counts?.[result] ?? 0;

                    return count > 0 ? (
                      <span key={result}>
                        {t(ThirdPartyRequestResultType[result])}: {count.toLocaleString()}
                      </span>
                    ) : null;
                  })}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};

const TrafficChart = ({ data }: { data: RequestStatistics[] }) => {
  const { t } = useTranslation();
  const sources = [...data].sort((a, b) => (b.receivedBytes ?? 0) - (a.receivedBytes ?? 0));
  const total = sources.reduce((sum, source) => sum + (source.receivedBytes ?? 0), 0);

  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-start">
      <div className="relative h-52 w-52 shrink-0">
        <Doughnut
          aria-label={t<string>("downloader.label.responseTraffic")}
          data={{
            labels: sources.map(getSourceName),
            datasets: [
              {
                data: sources.map((source) => source.receivedBytes ?? 0),
                backgroundColor: sources.map(getTrafficColor),
                borderWidth: 2,
              },
            ],
          }}
          options={{
            cutout: "65%",
            maintainAspectRatio: false,
            plugins: {
              legend: { display: false },
              tooltip: {
                callbacks: {
                  label: (context: TooltipItem<"doughnut">) =>
                    `${context.label}: ${humanFileSize(Number(context.raw), false, 2)}`,
                },
              },
            },
          }}
        />
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center text-center">
          <strong className="text-lg tabular-nums">{humanFileSize(total, false, 2)}</strong>
          <span className="text-xs text-foreground-500">
            {t("downloader.label.responseTraffic")}
          </span>
        </div>
      </div>
      <ul className="grid w-full min-w-0 flex-1 grid-cols-1 gap-x-5 gap-y-2 text-sm sm:grid-cols-2">
        {sources.map((source) => (
          <li
            key={source.id}
            className="flex min-w-0 items-center gap-2"
            data-testid={`traffic-source-${source.id}`}
          >
            <span
              aria-hidden="true"
              className="size-2 shrink-0 rounded-full"
              style={{ backgroundColor: getTrafficColor(source) }}
            />
            <span className="min-w-0 flex-1 truncate" title={getSourceName(source)}>
              {getSourceName(source)}
            </span>
            <strong className="whitespace-nowrap tabular-nums">
              {humanFileSize(source.receivedBytes ?? 0, false, 2)}
            </strong>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default RequestStatistics;
