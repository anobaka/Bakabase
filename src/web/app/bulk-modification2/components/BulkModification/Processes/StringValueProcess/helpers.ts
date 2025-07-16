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
      if (!value) {
        return 'Value is required';
      }
      break;
    case BulkModificationStringProcessOperation.AddToAnyPosition:
      if (index == undefined || index < 0 || !value) {
        return 'Value and index are required';
      }
      break;
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
      if (!(find != undefined && find.length > 0) || !value) {
        return 'Find and replace are required';
      }
      break;
  }

  return;
};
