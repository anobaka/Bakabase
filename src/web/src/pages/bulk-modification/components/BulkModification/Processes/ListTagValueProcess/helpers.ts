import type { ListTagProcessOptions } from "./models";

import { BulkModificationListTagProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationListTagProcessOperation,
  options: ListTagProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationListTagProcessOperation.Delete:
      return undefined;
    case BulkModificationListTagProcessOperation.SetWithFixedValue:
    case BulkModificationListTagProcessOperation.Append:
    case BulkModificationListTagProcessOperation.Prepend:
    case BulkModificationListTagProcessOperation.Remove:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
