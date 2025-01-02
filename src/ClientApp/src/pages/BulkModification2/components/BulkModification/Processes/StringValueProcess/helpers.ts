import type { StringProcessOptions } from './models';
import { BulkModificationStringProcessOperation } from '@/sdk/constants';
import type { RecursivePartial } from '@/components/types';
import { validate as validateValue } from '@/pages/BulkModification2/components/BulkModification/ProcessValue/helpers';

export const validate = (
  operation: BulkModificationStringProcessOperation,
  options?: StringProcessOptions,
): string | undefined => {
  if (operation == BulkModificationStringProcessOperation.Delete) {
    return;
  }

  if (!options) {
    return 'Please provide valid options';
  }

  const {
    value,
    count,
    index,
    isPositioningDirectionReversed,
    isOperationDirectionReversed,
    find,
  } = options;

  switch (operation) {
    case BulkModificationStringProcessOperation.SetWithFixedValue:
    case BulkModificationStringProcessOperation.AddToStart:
    case BulkModificationStringProcessOperation.AddToEnd:
      return validateValue(value);
    case BulkModificationStringProcessOperation.AddToAnyPosition:
      if (index == undefined || index < 0) {
        return 'Index is required';
      }
      return validateValue(value);
    case BulkModificationStringProcessOperation.RemoveFromStart:
    case BulkModificationStringProcessOperation.RemoveFromEnd:
      if (count == undefined || count < 0) {
        return 'Count is required';
      }
      break;
    case BulkModificationStringProcessOperation.RemoveFromAnyPosition:
      if (count == undefined || count < 0 || index == undefined || index < 0) {
        return 'Count and index are required';
      }
      break;
    case BulkModificationStringProcessOperation.ReplaceFromStart:
    case BulkModificationStringProcessOperation.ReplaceFromEnd:
    case BulkModificationStringProcessOperation.ReplaceFromAnyPosition:
    case BulkModificationStringProcessOperation.ReplaceWithRegex:
      if (!(find != undefined && find.length > 0)) {
        return 'Find is required';
      }
      break;
  }

  return;
};
