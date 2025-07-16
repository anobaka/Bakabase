import type {
  StringProcessOptions,
} from '../StringValueProcess/models';
import type {
  BulkModificationProcessorOptionsItemsFilterBy,
  BulkModificationProcessorValueType,
  BulkModificationStringProcessOperation,
} from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/BulkModification2/components/BulkModification/models';

export type ListStringValueProcessOptions = {
  value?: BulkModificationProcessValue;
  valueType?: BulkModificationProcessorValueType;
  isOperationDirectionReversed?: boolean;
  modifyOptions?: {
    filterBy: BulkModificationProcessorOptionsItemsFilterBy;
    filterValue?: string;
    operation: BulkModificationStringProcessOperation;
    options?: StringProcessOptions;
  };
};


export type EditingListStringValueProcessOptions = Omit<Partial<ListStringValueProcessOptions>, 'modifyOptions'> & {
  modifyOptions?: Partial<ListStringValueProcessOptions['modifyOptions']>;
};
