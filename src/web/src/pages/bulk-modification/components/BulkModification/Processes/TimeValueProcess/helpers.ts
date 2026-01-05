import type { TimeProcessOptions } from "./models";

import { BulkModificationTimeProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationTimeProcessOperation,
  options: TimeProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationTimeProcessOperation.Delete:
      return undefined;
    case BulkModificationTimeProcessOperation.SetWithFixedValue:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    case BulkModificationTimeProcessOperation.AddHours:
    case BulkModificationTimeProcessOperation.SubtractHours:
    case BulkModificationTimeProcessOperation.AddMinutes:
    case BulkModificationTimeProcessOperation.SubtractMinutes:
    case BulkModificationTimeProcessOperation.AddSeconds:
    case BulkModificationTimeProcessOperation.SubtractSeconds:
      if (options.amount === undefined || options.amount < 0) {
        return "bulkModification.validation.amountRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
