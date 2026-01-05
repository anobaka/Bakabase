import type { ListListStringProcessOptions } from "./models";

import { BulkModificationListListStringProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationListListStringProcessOperation,
  options: ListListStringProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationListListStringProcessOperation.Delete:
      return undefined;
    case BulkModificationListListStringProcessOperation.SetWithFixedValue:
    case BulkModificationListListStringProcessOperation.Append:
    case BulkModificationListListStringProcessOperation.Prepend:
    case BulkModificationListListStringProcessOperation.Remove:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
