import type { DownloadTaskAction, DownloadTaskDtoStatus, ThirdPartyId } from '@/sdk/constants';

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
  status: DownloadTaskDtoStatus;
  downloadPath?: string;
  current?: string;
  failureTimes: number;
  autoRetry: boolean;
  nextStartDt?: Date;
  availableActions: DownloadTaskAction[];
  displayName: string;
  canStart: boolean;
};
