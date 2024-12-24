import { validate as validateStringValueProcessOptions } from '../StringValueProcess/helpers';
import type { ListStringValueProcessOptions } from './models';
import {
  BulkModificationListStringProcessOperation,
  BulkModificationProcessorOptionsItemsFilterBy,
} from '@/sdk/constants';
import type { RecursivePartial } from '@/components/types';

export const validate = (
  operation: BulkModificationListStringProcessOperation,
  options?: RecursivePartial<ListStringValueProcessOptions>,
): string | undefined => {
  if (operation == BulkModificationListStringProcessOperation.Delete) {
    return;
  }

  if (!options) {
    return 'Options are required';
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
      if (!(value != undefined && value.length > 0 && valueType != undefined)) {
        return 'Value and value type are required';
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
