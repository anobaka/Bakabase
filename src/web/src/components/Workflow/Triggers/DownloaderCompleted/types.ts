/**
 * Mirrors DownloaderCompletedTrigger.Filter (backend).
 * Empty array = match every completed task; populated narrows by ThirdParty.
 */
export interface DownloaderCompletedFilter {
  thirdPartyIds: number[];
}
