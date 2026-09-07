import type { DownloadTaskAction, DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";

export type DownloadTask = {
  id: number;
  key: string;
  name?: string;
  thirdPartyId: ThirdPartyId;
  type: number;
  progress: number;
  downloadStatusUpdateDt: Date;
  interval?: number;
  startPage?: number;
  endPage?: number;
  message?: string;
  checkpoint?: string;
  status: DownloadTaskStatus;
  downloadPath?: string;
  current?: string;
  failureTimes: number;
  autoRetry: boolean;
  nextStartDt?: Date;
  availableActions: DownloadTaskAction[];
  displayName: string;
  canStart: boolean;
  createdAt: string;
  /** Serialized per-downloader options (e.g. ExHentai `{ "preferTorrent": true }`). */
  options?: string;
  /**
   * Read-only projection of what running the task has taught the server, already typed so the list
   * does not have to parse (or interpret) the per-source `options` blob. Absent when there is
   * nothing to show.
   */
  metadata?: {
    preferTorrent?: boolean;
    torrentFoundAt?: string;
    noTorrentCheckedAt?: string;
  };
};
