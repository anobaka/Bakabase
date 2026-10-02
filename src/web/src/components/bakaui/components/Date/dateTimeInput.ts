export interface ParsedDateTimeInput {
  iso: string;
  hasExplicitTimezone: boolean;
}

const pad = (value: number, length = 2) => String(value).padStart(length, "0");
const timezoneSuffix = /\s*(Z|UTC(?:\s*[+-]\d{2}:?\d{2})?|[+-]\d{2}:?\d{2})$/i;

/** Parse complete year-first dates without relying on browser-specific string date parsing. */
export const parseDateTimeInput = (value: string): ParsedDateTimeInput | undefined => {
  let text = value.trim().replace(/：/g, ":");

  if (!text || text.length > 128) return undefined;

  const zone = timezoneSuffix.exec(text);
  let offsetMinutes: number | undefined;

  if (zone) {
    const offset = /([+-])(\d{2}):?(\d{2})$/.exec(zone[1]);

    if (offset) {
      const hours = Number(offset[2]);
      const minutes = Number(offset[3]);

      // DateTimeOffset on the server accepts offsets through +/-14:00.
      if (hours > 14 || minutes > 59 || (hours === 14 && minutes !== 0)) return undefined;

      offsetMinutes = (hours * 60 + minutes) * (offset[1] === "-" ? -1 : 1);
    } else {
      offsetMinutes = 0;
    }
    text = text.slice(0, zone.index).trim();
  }

  const separated = /^(\d{4})([-/.])(\d{1,2})\2(\d{1,2})(?:(?:[Tt]|\s+)(.+))?$/.exec(text);
  const chinese = /^(\d{4})年\s*(\d{1,2})月\s*(\d{1,2})日(?:\s*(.+))?$/.exec(text);
  const compact = /^(\d{4})(\d{2})(\d{2})(?:(\d{2})(\d{2})(\d{2})?)?$/.exec(text);
  let year: number;
  let month: number;
  let day: number;
  let clock: string | undefined;

  if (separated) {
    year = Number(separated[1]);
    month = Number(separated[3]);
    day = Number(separated[4]);
    clock = separated[5];
  } else if (chinese) {
    year = Number(chinese[1]);
    month = Number(chinese[2]);
    day = Number(chinese[3]);
    clock = chinese[4];
  } else if (compact) {
    year = Number(compact[1]);
    month = Number(compact[2]);
    day = Number(compact[3]);
    clock = compact[4]
      ? `${compact[4]}:${compact[5]}${compact[6] ? `:${compact[6]}` : ""}`
      : undefined;
  } else {
    return undefined;
  }

  // A date alone means local midnight; a timezone requires an explicit clock.
  if (year < 1 || year > 9999 || (!clock && offsetMinutes !== undefined)) return undefined;

  let hour = 0;
  let minute = 0;
  let second = 0;
  let fraction = "";

  if (clock) {
    const colon = /^(\d{1,2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?$/.exec(clock);
    const units =
      /^(\d{1,2})\s*时(?:\s*(\d{1,2})\s*分)?(?:\s*(\d{1,2})(?:\.(\d{1,7}))?\s*秒)?$/.exec(clock);
    const parts = colon ?? units;

    if (!parts) return undefined;

    hour = Number(parts[1]);
    minute = Number(parts[2] ?? 0);
    second = Number(parts[3] ?? 0);
    fraction = parts[4] ?? "";
  }

  if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59)
    return undefined;

  const millisecond = Number(fraction.slice(0, 3).padEnd(3, "0"));
  const calendar = new Date(0);

  // Date.UTC and new Date(year, ...) interpret years 0..99 as 1900..1999.
  calendar.setUTCFullYear(year, month - 1, day);
  calendar.setUTCHours(hour, minute, second, millisecond);
  if (
    calendar.getUTCFullYear() !== year ||
    calendar.getUTCMonth() !== month - 1 ||
    calendar.getUTCDate() !== day
  )
    return undefined;

  let date: Date;

  if (offsetMinutes !== undefined) {
    date = new Date(calendar.getTime() - offsetMinutes * 60_000);
  } else {
    date = new Date(0);
    date.setFullYear(year, month - 1, day);
    date.setHours(hour, minute, second, millisecond);
    // Reject daylight-saving gaps and skipped dates rather than shifting the user's time.
    if (
      date.getFullYear() !== year ||
      date.getMonth() !== month - 1 ||
      date.getDate() !== day ||
      date.getHours() !== hour ||
      date.getMinutes() !== minute ||
      date.getSeconds() !== second ||
      date.getMilliseconds() !== millisecond
    )
      return undefined;
  }

  // The UTC instant must also fit the server's DateTime range.
  if (date.getUTCFullYear() < 1 || date.getUTCFullYear() > 9999) return undefined;

  return {
    iso: fraction ? date.toISOString().replace(/\.\d{3}Z$/, `.${fraction}Z`) : date.toISOString(),
    hasExplicitTimezone: offsetMinutes !== undefined,
  };
};

/** Show local wall time while keeping the fractional precision supplied by stored timestamps. */
export const formatDateTimeInput = (value: string | number): string => {
  const parsed = typeof value === "string" ? parseDateTimeInput(value) : undefined;

  if (typeof value === "string" && !parsed) return "";

  const date = new Date(parsed?.iso ?? value);

  if (!Number.isFinite(date.getTime())) return "";
  // A valid UTC boundary can fall outside the server's year range in local time.
  if (date.getFullYear() < 1 || date.getFullYear() > 9999) return parsed?.iso ?? "";

  const fraction = parsed?.iso.match(/\.(\d+)Z$/)?.[1];
  const clockText = typeof value === "string" ? value.trim().replace(timezoneSuffix, "") : "";
  // Distinguish fractional seconds from the dots in a date such as 2026.9.05.
  const sourceFraction =
    /[:：]\d{2}\.(\d{1,7})$/.exec(clockText)?.[1] ?? /\.(\d{1,7})\s*秒$/.exec(clockText)?.[1];

  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${sourceFraction && fraction ? `.${fraction}` : ""}`;
};
