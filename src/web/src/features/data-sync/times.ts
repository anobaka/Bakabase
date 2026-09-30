import type { TFunction } from "i18next";

import { millisecondsUntil, parseServerTime } from "@/core/serverTime";

/*
 * Every time `/data-sync` answers is UTC, written by the server as naked digits
 * (`2026-09-01 08:00:00.000`). They are read here and nowhere else — never with `Date.parse`,
 * which reads the naked form as local time and puts every time east of Greenwich hours in the
 * past (spec §2.10, `.claude/rules/federation.md`).
 */

/** The instant a server time names, or null when it names none. */
export const serverTime = (value?: string | null) => parseServerTime(value);

/** Milliseconds since a server time, never negative; null when it names none. */
export const millisecondsSince = (value?: string | null, now: number = Date.now()) => {
  const at = parseServerTime(value);

  return at == null ? null : Math.max(0, now - at.getTime());
};

/** Whether a server deadline has passed. An unreadable deadline has. */
export const hasPassed = (value?: string | null, now: number = Date.now()) =>
  millisecondsUntil(value, now) === 0;

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * How long ago a server time was, in the page's words: "just now", "5 min ago", "2 h ago",
 * "3 d ago" — or "never" for none.
 */
export const timeAgo = (t: TFunction, value?: string | null, now: number = Date.now()) => {
  const elapsed = millisecondsSince(value, now);

  if (elapsed == null) return t("dataSync.time.never");
  if (elapsed < MINUTE) return t("dataSync.time.justNow");
  if (elapsed < HOUR) return t("dataSync.time.minutes", { count: Math.floor(elapsed / MINUTE) });
  if (elapsed < DAY) return t("dataSync.time.hours", { count: Math.floor(elapsed / HOUR) });

  return t("dataSync.time.days", { count: Math.floor(elapsed / DAY) });
};

/** Whole minutes left until a server deadline, rounded up so a live one never reads 0. */
export const minutesLeft = (value?: string | null, now: number = Date.now()) =>
  Math.ceil(millisecondsUntil(value, now) / MINUTE);

/** A server time as this browser's local date and time, or "" for none. */
export const localDateTime = (value?: string | null) => {
  const at = parseServerTime(value);

  return at ? at.toLocaleString() : "";
};
