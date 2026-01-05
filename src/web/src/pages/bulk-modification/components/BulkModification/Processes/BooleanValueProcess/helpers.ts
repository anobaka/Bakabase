import type { BooleanProcessOptions } from "./models";

import { BulkModificationBooleanProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationBooleanProcessOperation,
  options: BooleanProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationBooleanProcessOperation.Delete:
    case BulkModificationBooleanProcessOperation.Toggle:
      return undefined;
    case BulkModificationBooleanProcessOperation.SetWithFixedValue:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
