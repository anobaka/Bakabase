import type { ChartData, ChartOptions } from "chart.js";
import { Chart as ChartJS, LineElement, LinearScale, PointElement, Tooltip } from "chart.js";
import { Line } from "react-chartjs-2";
import { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { formatRate, metrics, partial, rate, sampleTime, taskTrends } from "./model";
import type { Rates, Throughput } from "./model";

ChartJS.register(LinearScale, PointElement, LineElement, Tooltip);
type Point = { x: number; y: number | null; partial: boolean };
type Trend = ReturnType<typeof taskTrends>;

function TaskChart({
  id,
  ticks,
  end,
  windowSeconds,
}: {
  id: number;
  ticks: Trend["ticks"];
  end: number;
  windowSeconds: number;
}) {
  const { i18n, t } = useTranslation();
  const time = useMemo(
    () =>
      new Intl.DateTimeFormat(i18n.language, {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      }),
    [i18n.language],
  );
  const data = useMemo<ChartData<"line", Point[]>>(
    () => ({
      datasets: metrics.map((metric) => ({
        label: t(`acquisition.throughput.${metric.key}`),
        data: ticks.map(({ time, tasks }) => ({
          x: time,
          y: rate(tasks.get(id), metric.key),
          partial: partial(tasks.get(id), metric.key),
        })),
        borderColor: metric.color,
        backgroundColor: metric.color,
        borderDash: [...metric.dash],
        borderWidth: 2,
        spanGaps: false,
        pointRadius: (point) => ((point.raw as Point | undefined)?.partial ? 3 : 1.5),
        pointStyle: (point) => ((point.raw as Point | undefined)?.partial ? "triangle" : "circle"),
        pointHoverRadius: 4,
      })),
    }),
    [id, ticks, t],
  );
  const options = useMemo<ChartOptions<"line">>(
    () => ({
      animation: false,
      responsive: true,
      maintainAspectRatio: false,
      parsing: false,
      interaction: { mode: "nearest", axis: "x", intersect: false },
      scales: {
        x: {
          type: "linear",
          min: end - windowSeconds * 1000,
          max: end,
          grid: { display: false },
          ticks: {
            maxTicksLimit: 4,
            maxRotation: 0,
            callback: (value) => time.format(Number(value)),
          },
        },
        y: {
          type: "linear",
          beginAtZero: true,
          suggestedMax: 1,
          grid: { color: "rgba(125, 130, 142, .18)" },
          ticks: { maxTicksLimit: 4, precision: 0, callback: (value) => formatRate(Number(value)) },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            title: (items) => (items.length ? time.format((items[0].raw as Point).x) : ""),
            label: (item) => {
              const point = item.raw as Point;
              return `${item.dataset.label}  ${point.partial && point.y !== null ? "≥ " : ""}${formatRate(point.y)}`;
            },
          },
        },
      },
    }),
    [end, windowSeconds, time],
  );

  return (
    <div className="relative h-36 w-full min-w-0">
      <Line
        aria-label={t("acquisition.throughput.taskChart", { id })}
        role="img"
        data={data}
        options={options}
      />
    </div>
  );
}

export function RateValues({ rates }: { rates?: Rates | null }) {
  const { t } = useTranslation();
  return (
    <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
      {metrics.map((metric) => {
        const value = rate(rates, metric.key);
        return (
          <div key={metric.key} className="flex items-center gap-1.5">
            <dt className="flex items-center gap-1 text-default-500">
              <span
                aria-hidden
                className="h-0 w-3 border-t-2"
                style={{
                  borderColor: metric.color,
                  borderTopStyle:
                    metric.key === "download"
                      ? "solid"
                      : metric.key === "read"
                        ? "dashed"
                        : "dotted",
                }}
              />
              {t(`acquisition.throughput.${metric.key}`)}
            </dt>
            <dd className="tabular-nums">
              {value !== null && partial(rates, metric.key) ? "≥ " : ""}
              {formatRate(value)}
            </dd>
          </div>
        );
      })}
    </dl>
  );
}

export default function ThroughputHistory({
  snapshot,
  current,
}: {
  snapshot: Throughput;
  current: boolean;
}) {
  const { t } = useTranslation();
  const interval = snapshot.intervalSeconds || 5;
  const trend = useMemo(
    () => taskTrends(snapshot.history ?? [], interval),
    [snapshot.history, interval],
  );
  const latest = snapshot.latest;
  const last = trend.ticks[trend.ticks.length - 1];
  if (!latest || !last) return <p>{t("acquisition.throughput.noSamples")}</p>;
  const ids = [...trend.titles.keys()].sort((a, b) => a - b);
  if (!ids.length)
    return (
      <p className="py-4 text-center text-sm text-default-500">
        {t("acquisition.throughput.empty")}
      </p>
    );
  const end = sampleTime(latest);

  return (
    <div className="grid min-w-0 grid-cols-1 gap-3 md:grid-cols-2">
      {ids.map((id) => (
        <section key={id} className="min-w-0 space-y-2 rounded-lg border border-default-200 p-3">
          <h3 className="break-words text-sm font-semibold">
            #{id} {trend.titles.get(id)}
          </h3>
          <RateValues rates={current && last.time === end ? last.tasks.get(id) : undefined} />
          <TaskChart
            id={id}
            ticks={trend.ticks}
            end={last.time}
            windowSeconds={snapshot.windowSeconds || 300}
          />
        </section>
      ))}
    </div>
  );
}
