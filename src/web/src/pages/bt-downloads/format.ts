export function formatBtBytes(value?: number | null): string {
  if (value == null || !Number.isFinite(value) || value < 0) return "—";
  if (value < 1024) return `${Math.floor(value)} B`;
  const power = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 4);
  return `${(value / 1024 ** power).toFixed(1)} ${["B", "KiB", "MiB", "GiB", "TiB"][power]}`;
}
