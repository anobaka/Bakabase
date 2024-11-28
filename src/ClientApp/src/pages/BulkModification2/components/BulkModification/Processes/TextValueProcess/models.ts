import type { BulkModificationProcessorValueType } from '@/sdk/constants';

export type TextProcessOptions = {
  value?: string;
  index?: number;
  isOperationDirectionReversed?: boolean;
  isPositioningDirectionReversed?: boolean;
  count?: number;
  find?: string;
  replace?: string;
  valueType?: BulkModificationProcessorValueType;
};
