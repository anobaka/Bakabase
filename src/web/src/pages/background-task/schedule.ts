import type { BakabaseAbstractionsModelsDbBTaskDbModel as TaskOption } from "@/sdk/Api";

import moment from "moment";

export const intervalUnits = { seconds: 1, minutes: 60, hours: 3600, days: 86400 } as const;
export type IntervalUnit = keyof typeof intervalUnits;

export function intervalInput(interval?: string): { amount: string; unit: IntervalUnit } {
  const seconds = interval ? moment.duration(interval).asSeconds() : 300;
  const valid = Number.isFinite(seconds) && seconds > 0 ? seconds : 300;
  const unit =
    (["days", "hours", "minutes"] as const).find(
      (candidate) => valid % intervalUnits[candidate] === 0,
    ) ?? "seconds";

  return { amount: String(valid / intervalUnits[unit]), unit };
}

/** Serialize a positive duration without wrapping after 24 hours like a clock input. */
export function intervalValue(amount: string, unit: IntervalUnit): string | undefined {
  if (!amount.trim()) return undefined;
  const seconds = Number(amount) * intervalUnits[unit];

  if (!Number.isSafeInteger(seconds) || seconds <= 0 || seconds > 922337203685) return undefined;
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const clock = [hours, minutes, seconds % 60]
    .map((part) => String(part).padStart(2, "0"))
    .join(":");

  return `${days ? `${days}.` : ""}${clock}`;
}

/** PATCH replaces the list, so retain saved options for tasks not currently registered. */
export function mergeTaskOption(saved: TaskOption[] | undefined, edited: TaskOption): TaskOption[] {
  return [...(saved ?? []).filter((option) => option.id !== edited.id), edited];
}
