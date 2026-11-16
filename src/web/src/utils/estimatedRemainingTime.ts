import type { TFunction } from "i18next";

export function estimatedRemainingLabel(seconds: number | null | undefined, t: TFunction): string | undefined {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return undefined;

  let remaining = Math.ceil(seconds);
  const units = [
    [86400, t<string>("datetime.duration.day")],
    [3600, t<string>("datetime.duration.hour")],
    [60, t<string>("datetime.duration.minute")],
    [1, t<string>("datetime.duration.second")],
  ] as const;
  const parts = units.flatMap(([size, label]) => {
    const value = Math.floor(remaining / size);

    remaining %= size;

    return value > 0 ? [`${value}${label}`] : [];
  });
  const duration = parts.join(" ") || `0${t<string>("datetime.duration.second")}`;

  return `${t<string>("downloader.label.estimatedRemaining")} ${duration}`;
}
