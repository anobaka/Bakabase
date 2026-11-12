import type { TFunction } from "i18next";

/** Typed local failures, including old diagnostics whose OS cause was not retained. */
export function localStorageProblemCode(message?: string | null): string | undefined {
  const codes = message?.match(/\b[A-Z][A-Z0-9_]{2,}\b/g) ?? [];
  return (
    codes.find((code) =>
      [
        "LOCAL_STORAGE_FULL",
        "LOCAL_STORAGE_RESERVE_LOW",
        "LOCAL_STORAGE_WRITE_FAILED",
        "LOCAL_STORAGE_ACCESS_DENIED",
        "LOCAL_STORAGE_UNAVAILABLE",
      ].includes(code),
    ) ??
    codes.find((code) =>
      [
        "BAIDU_OUTPUT_DIRECTORY_FAILED",
        "PUBLICATION_IO_ERROR",
        "RECOVERY_STORAGE_UNAVAILABLE",
      ].includes(code),
    )
  );
}

/** Stable provider error codes can be localized without hiding the original diagnostic. */
export function downloadProblemSummary(message: string, t: TFunction): string | undefined {
  const codes = message.match(/\b[A-Z][A-Z0-9_]{2,}\b/g) ?? [];
  const storageCode = localStorageProblemCode(message);
  for (const code of storageCode ? [storageCode, ...codes] : codes) {
    const key = `cloudDownload.problemCode.${code}`;
    const text = t<string>(key, { defaultValue: "" });
    if (text && text !== key) return text;
  }
  return undefined;
}

export function downloadProblemLabel(message: string, t: TFunction): string {
  const key = `cloudDownload.problem.${message}`;
  const label = t<string>(key, { defaultValue: message, nsSeparator: false });
  return downloadProblemSummary(message, t) ?? (label === key ? message : label);
}
