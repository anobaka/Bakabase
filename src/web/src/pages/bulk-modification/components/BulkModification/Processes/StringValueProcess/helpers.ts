import type { StringProcessOptions } from "./models";

import { BulkModificationStringProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationStringProcessOperation,
  options?: StringProcessOptions,
): string | undefined => {
  if (operation == BulkModificationStringProcessOperation.Delete) {
    return;
  }

  if (!options) {
    return "bulkModification.validation.optionsRequired";
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
        return "bulkModification.validation.valueRequired";
      }
      break;
    case BulkModificationStringProcessOperation.AddToAnyPosition:
      if (index == undefined || index < 0 || !value) {
        return "bulkModification.validation.valueAndIndexRequired";
      }
      break;
    case BulkModificationStringProcessOperation.RemoveFromStart:
    case BulkModificationStringProcessOperation.RemoveFromEnd:
      if (count == undefined || count < 0) {
        return "bulkModification.validation.countRequired";
      }
      break;
    case BulkModificationStringProcessOperation.RemoveFromAnyPosition:
      if (count == undefined || count < 0 || index == undefined || index < 0) {
        return "bulkModification.validation.countAndIndexRequired";
      }
      break;
    case BulkModificationStringProcessOperation.ReplaceFromStart:
    case BulkModificationStringProcessOperation.ReplaceFromEnd:
    case BulkModificationStringProcessOperation.ReplaceFromAnyPosition:
    case BulkModificationStringProcessOperation.ReplaceWithRegex:
      if (!(find != undefined && find.length > 0) || !value) {
        return "bulkModification.validation.findAndReplaceRequired";
      }
      break;
  }

  return;
};
