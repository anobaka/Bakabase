import type {
  StringProcessOptions,
} from '../StringValueProcess/models';
import type {
  BulkModificationProcessorOptionsItemsFilterBy,
  BulkModificationProcessorValueType,
  BulkModificationStringProcessOperation,
} from '@/sdk/constants';

export type ListStringValueProcessOptions = {
  value?: string;
  valueType?: BulkModificationProcessorValueType;
  isOperationDirectionReversed?: boolean;
  modifyOptions?: {
    filterBy: BulkModificationProcessorOptionsItemsFilterBy;
    filterValue?: string;
    operation: BulkModificationStringProcessOperation;
    options?: StringProcessOptions;
  };
};
