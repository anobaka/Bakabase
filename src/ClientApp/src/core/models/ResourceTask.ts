import type { ResourceTaskOperationOnComplete, ResourceTaskType } from '@/sdk/constants';

export default interface ResourceTask{
  id: number;
  percentage: number;
  operationOnComplete: ResourceTaskOperationOnComplete;
  summary?: string;
  type: ResourceTaskType;
  error?: string;
  backgroundTaskId: string;
}
