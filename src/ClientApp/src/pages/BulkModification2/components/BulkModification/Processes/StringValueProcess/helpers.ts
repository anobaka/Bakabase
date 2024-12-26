import type { StringProcessOptions } from './models';
import { BulkModificationStringProcessOperation } from '@/sdk/constants';
import type { RecursivePartial } from '@/components/types';

export const validate = (
  operation: BulkModificationStringProcessOperation,
  options?: RecursivePartial<StringProcessOptions>,
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
      if (value == undefined || value.type == undefined || value.editorPropertyType == undefined || value.value == undefined) {
        return 'Value is required';
      }
      break;
    case BulkModificationStringProcessOperation.AddToAnyPosition:
      if (value == undefined || value.value == undefined || value.value.length > 0 || index == undefined || index < 0) {
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
      if (!(find != undefined && find.length > 0)) {
        return 'Find is required';
      }
      break;
  }

  return;
};
