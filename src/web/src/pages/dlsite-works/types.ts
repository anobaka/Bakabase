export interface DLsiteWork {
  id: number;
  workId: string;
  title?: string;
  circle?: string;
  workType?: string;
  coverUrl?: string;
  metadataJson?: string;
  metadataFetchedAt?: string;
  drmKey?: string;
  account?: string;
  isPurchased: boolean;
  isDownloaded: boolean;
  isHidden: boolean;
  localPath?: string;
  resourceId?: number;
  createdAt: string;
  updatedAt: string;
}

export const SYNC_TASK_ID = "SyncDLsite";
export const DOWNLOAD_TASK_ID_PREFIX = "DownloadDLsite_";
export const SCAN_TASK_ID = "ScanDLsiteFolder";
export const DLSITE_WORK_URL = "https://www.dlsite.com/maniax/work/=/product_id/";
