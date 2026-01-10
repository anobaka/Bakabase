import type { DecimalProcessOptions } from "./models";

import { BulkModificationDecimalProcessOperation } from "@/sdk/constants";

export const validate = (
  operation: BulkModificationDecimalProcessOperation,
  options: DecimalProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationDecimalProcessOperation.Delete:
    case BulkModificationDecimalProcessOperation.Ceil:
    case BulkModificationDecimalProcessOperation.Floor:
      return undefined;
    case BulkModificationDecimalProcessOperation.SetWithFixedValue:
    case BulkModificationDecimalProcessOperation.Add:
    case BulkModificationDecimalProcessOperation.Subtract:
    case BulkModificationDecimalProcessOperation.Multiply:
    case BulkModificationDecimalProcessOperation.Divide:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    case BulkModificationDecimalProcessOperation.Round:
      if (options.decimalPlaces === undefined || options.decimalPlaces < 0) {
        return "bulkModification.validation.decimalPlacesRequired";
      }
      return undefined;
    default:
      return undefined;
  }
};
