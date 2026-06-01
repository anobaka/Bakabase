import type { NotificationViewModel } from "@/stores/notifications";

export type NotificationGroupKey = "today" | "thisWeek" | "earlier";

const MS_PER_DAY = 86_400_000;

function startOfToday(): number {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d.getTime();
}

export function groupByDate(items: NotificationViewModel[]): {
  key: NotificationGroupKey;
  items: NotificationViewModel[];
}[] {
  const today = startOfToday();
  const weekAgo = today - 6 * MS_PER_DAY;

  const buckets: Record<NotificationGroupKey, NotificationViewModel[]> = {
    today: [],
    thisWeek: [],
    earlier: [],
  };

  for (const n of items) {
    const t = new Date(n.createdAt).getTime();
    if (t >= today) buckets.today.push(n);
    else if (t >= weekAgo) buckets.thisWeek.push(n);
    else buckets.earlier.push(n);
  }

  return (["today", "thisWeek", "earlier"] as const)
    .filter((k) => buckets[k].length > 0)
    .map((k) => ({ key: k, items: buckets[k] }));
}
