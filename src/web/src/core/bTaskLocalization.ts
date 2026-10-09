import type { BTaskLocalizedTexts } from "./models/BTask";

const fields = [
  "name",
  "description",
  "process",
  "message",
  "briefError",
  "messageOnInterruption",
  "reasonForUnableToStart",
] as const;

// A file row may cache an already projected store task. Retain its original fallback
// without adding metadata to the task payload or keeping removed tasks alive.
const sourceByProjection = new WeakMap<object, object>();

/** The browser language determines task text, independently of the connected server. */
export function localizeBTask<
  T extends { localizedTexts?: Record<string, BTaskLocalizedTexts> | null },
>(task: T, language: string): T {
  const source = (sourceByProjection.get(task) as T | undefined) ?? task;
  const culture = (language || "en").toLowerCase().replace(/_/g, "-");
  const key = culture === "cn" || culture.startsWith("zh") ? "cn" : "en";
  const texts = source.localizedTexts?.[key];

  if (!texts) return source;
  const result = { ...source };

  for (const field of fields) {
    // An explicit null clears the text; an absent field supports older/partial payloads.
    if (Object.prototype.hasOwnProperty.call(texts, field)) {
      (result as Record<string, unknown>)[field] = texts[field] ?? undefined;
    }
  }

  sourceByProjection.set(result, source);

  return result;
}
