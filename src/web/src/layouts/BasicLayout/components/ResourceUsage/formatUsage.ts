const units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

export const formatUsageUpdatedAt = (value: string): string => {
  // The shared server serializer omits the offset. This measurement is always UTC.
  const utc = /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?$/.test(value)
    ? `${value.replace(" ", "T")}Z`
    : value;
  const date = new Date(utc);

  return Number.isFinite(date.getTime()) ? date.toLocaleTimeString() : "—";
};

export const formatUsageBytes = (bytes?: number | null): string => {
  if (bytes == null || !Number.isFinite(bytes) || bytes < 0) return "—";
  let value = bytes;
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  return `${value.toLocaleString(undefined, { maximumFractionDigits: unit === 0 || value >= 100 ? 0 : 1 })} ${units[unit]}`;
};
