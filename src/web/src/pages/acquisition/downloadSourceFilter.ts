import type { TFunction } from "i18next";
import { downloadSourceLabel } from "@/components/DownloadSource";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";

export const unsupportedDownloadSource = "unsupported";

/** Download Center protocol/site keys and the acquisition-only unsupported source filter. */
export const acquisitionDownloadSources = [
  "bt", "http",
  ...[...new Set([ThirdPartyId.ExHentai, ...CloudDownloadSources.map(source => source.thirdPartyId)])]
    .sort((a, b) => a - b).map(id => `site:${id}`),
  unsupportedDownloadSource,
];

export function normalizeAcquisitionDownloadSource(value: unknown): string | undefined {
  return typeof value === "string" && acquisitionDownloadSources.includes(value) ? value : undefined;
}

export function acquisitionDownloadSourceLabel(source: string, t: TFunction): string {
  return source === unsupportedDownloadSource
    ? t("acquisition.filters.sourceUnsupported")
    : downloadSourceLabel(source, t);
}
