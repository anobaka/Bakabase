const units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

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
