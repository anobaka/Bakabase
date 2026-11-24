import { formatBtBytes } from "@/pages/bt-downloads/format";
import type BApi from "@/sdk/BApi";

export type Throughput = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionThroughput>>["data"]
>;
export type Sample = NonNullable<Throughput["latest"]>;
export type Rates = NonNullable<Sample["totals"]>;
export type Metric = "download" | "read" | "write";

export const metrics = [
  { key: "download", color: "#477dea", dash: [] },
  { key: "read", color: "#138b74", dash: [6, 3] },
  { key: "write", color: "#d48524", dash: [2, 3] },
] as const;

export function rate(rates: Rates | null | undefined, metric: Metric) {
  const value = rates?.[`${metric}BytesPerSecond`];

  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : null;
}

export function partial(rates: Rates | null | undefined, metric: Metric) {
  return Boolean(metric === "download" ? rates?.downloadPartial : rates?.fileIoPartial);
}

export function formatRate(value: number | null) {
  if (value === null) return "—";
  if (value > 0 && value < 1)
    return `${value.toLocaleString(undefined, { maximumSignificantDigits: 2 })} B/s`;
  return `${formatBtBytes(value)}/s`;
}

export function formatReportedRate(metric: Metric, value: number | null, qualified: boolean) {
  const formatted = formatRate(value);
  if (value === null || !qualified) return formatted;
  return metric === "download" ? `≥ ${formatted}` : `${formatted}*`;
}

export function sampleTime(sample: Sample) {
  const value = sample.sampledAt ?? "";
  // Bakabase's shared serializer can return UTC without an offset.
  const utc = /^\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:\.\d+)?$/.test(value)
    ? value.replace(" ", "T") + "Z"
    : value;

  return Date.parse(utc);
}

export function contributes(rates?: Rates | null) {
  return metrics.some(({ key }) => (rate(rates, key) ?? 0) > 0);
}

function fullyMeasuredZero(rates?: Rates | null) {
  return metrics.every(({ key }) => rate(rates, key) === 0 && !partial(rates, key));
}

export function taskTrends(samples: Sample[], intervalSeconds: number) {
  const sorted = samples
    .map((sample) => ({ sample, time: sampleTime(sample) }))
    .filter((row) => Number.isFinite(row.time))
    .sort((a, b) => a.time - b.time);
  const byTime = new Map(sorted.map((row) => [row.time, row.sample]));
  const ticks: { time: number; tasks: Map<number, Rates | undefined> }[] = [];
  const titles = new Map<number, string>();
  let previous: number | undefined;
  const contributors = new Set<number>();
  let observedSamples = 0;
  let zeroSamples = 0;

  for (const [time, sample] of byTime) {
    // An absent bucket means a gap in observation, even if both adjacent buckets have data.
    if (previous !== undefined && time - previous > intervalSeconds * 1500)
      ticks.push({ time: previous + intervalSeconds * 1000, tasks: new Map() });
    const tasks = new Map<number, Rates | undefined>();
    for (const task of sample.tasks ?? []) {
      if (task.taskId == null) continue;
      tasks.set(task.taskId, task.rates ?? undefined);
      if (contributes(task.rates)) contributors.add(task.taskId);
      titles.set(task.taskId, task.title?.trim() || titles.get(task.taskId) || "");
    }
    const taskRates = [...tasks.values()];
    if (
      [sample.totals, ...taskRates].some((row) =>
        metrics.some(({ key }) => rate(row, key) !== null),
      )
    )
      observedSamples++;
    if (fullyMeasuredZero(sample.totals) && taskRates.every(fullyMeasuredZero)) zeroSamples++;
    ticks.push({ time, tasks });
    previous = time;
  }

  return { ticks, titles, contributors, observedSamples, zeroSamples };
}
