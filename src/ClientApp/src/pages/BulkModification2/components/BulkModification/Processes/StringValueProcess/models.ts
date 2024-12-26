import type { BulkModificationProcessorValueType } from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/BulkModification2/components/BulkModification/models';

export type StringProcessOptions = {
  value?: BulkModificationProcessValue;
  index?: number;
  isOperationDirectionReversed?: boolean;
  isPositioningDirectionReversed?: boolean;
  count?: number;
  find?: string;
};
