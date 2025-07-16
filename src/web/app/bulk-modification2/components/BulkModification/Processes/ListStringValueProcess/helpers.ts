import { validate as validateStringValueProcessOptions } from '../StringValueProcess/helpers';
import type { EditingListStringValueProcessOptions } from './models';
import {
  BulkModificationListStringProcessOperation,
  BulkModificationProcessorOptionsItemsFilterBy,
} from '@/sdk/constants';
import type { RecursivePartial } from '@/components/types';
import { validate as validateValue } from '@/pages/BulkModification2/components/BulkModification/ProcessValue/helpers';

export const validate = (
  operation: BulkModificationListStringProcessOperation,
  options?: EditingListStringValueProcessOptions,
): string | undefined => {
  if (operation == BulkModificationListStringProcessOperation.Delete) {
    return;
  }

  if (!options) {
    return 'Please provide valid options';
  }

  const {
    value,
    valueType,
    isOperationDirectionReversed,
    modifyOptions,
  } = options;

  switch (operation) {
    case BulkModificationListStringProcessOperation.SetWithFixedValue:
    case BulkModificationListStringProcessOperation.Append:
    case BulkModificationListStringProcessOperation.Prepend:
      if (!value) {
        return 'Value is required';
      }
      break;
    case BulkModificationListStringProcessOperation.Modify:
      if (modifyOptions == undefined) {
        return 'Modify options are required';
      }

      if (modifyOptions.filterBy != BulkModificationProcessorOptionsItemsFilterBy.All && (modifyOptions.filterValue == undefined || modifyOptions.filterValue.length == 0)) {
        return 'Filter value is required';
      }

      if (modifyOptions.operation == undefined) {
        return 'Operation is required';
      }

      return validateStringValueProcessOptions(modifyOptions.operation, modifyOptions.options);
  }

  return;
};
