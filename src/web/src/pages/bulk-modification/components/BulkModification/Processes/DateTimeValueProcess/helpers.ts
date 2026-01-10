import type { DateTimeProcessOptions } from "./models";

import { BulkModificationDateTimeProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationDateTimeProcessOperation,
  options: DateTimeProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationDateTimeProcessOperation.Delete:
    case BulkModificationDateTimeProcessOperation.SetToNow:
      return undefined;
    case BulkModificationDateTimeProcessOperation.SetWithFixedValue:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    case BulkModificationDateTimeProcessOperation.AddDays:
    case BulkModificationDateTimeProcessOperation.SubtractDays:
    case BulkModificationDateTimeProcessOperation.AddMonths:
    case BulkModificationDateTimeProcessOperation.SubtractMonths:
    case BulkModificationDateTimeProcessOperation.AddYears:
    case BulkModificationDateTimeProcessOperation.SubtractYears:
      if (options.amount === undefined || options.amount < 0) {
        return "bulkModification.validation.amountRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
