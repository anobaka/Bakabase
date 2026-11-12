import type { TFunction } from "i18next";

const reasonNamespaces = [
  "acquisition.localBatch.reason",
  "acquisition.recovery.reason",
  "acquisition.plan.issue",
  "acquire.requirement",
  "acquisition.storageOverride.reason",
  "acquisition.runtime.problem",
  "acquisition.recovery.error",
  "cloudDownload.problemCode",
];

/** The application's missing-key handler returns the key even when defaultValue is set.
 * Resolve known namespaces explicitly so inherited recovery reasons and future codes never
 * appear as a raw translation key in the queue dialog. */
export function localBatchReasonLabel(t: TFunction, code?: string | null): string {
  if (code && /^[A-Z][A-Z0-9_]{2,100}$/.test(code)) {
    for (const namespace of reasonNamespaces) {
      const key = `${namespace}.${code}`;
      const value = t<string>(key);
      if (typeof value === "string" && value.trim() && value !== key) return value;
    }
  }
  return t<string>("acquisition.localBatch.reason.unknown");
}
