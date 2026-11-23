import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";

/** The same protocol/site keys used by Download Center; acquisition supports these source families. */
export const acquisitionDownloadSources = [
  "bt", "http",
  ...[...new Set([ThirdPartyId.ExHentai, ...CloudDownloadSources.map(source => source.thirdPartyId)])]
    .sort((a, b) => a - b).map(id => `site:${id}`),
];

export function normalizeAcquisitionDownloadSource(value: unknown): string | undefined {
  return typeof value === "string" && acquisitionDownloadSources.includes(value) ? value : undefined;
}
