import type { EditingListStringValueProcessOptions } from "./models";

import { validate as validateStringValueProcessOptions } from "../StringValueProcess/helpers";

import {
  BulkModificationListStringProcessOperation,
  BulkModificationProcessorOptionsItemsFilterBy,
} from "@/sdk/constants";

export const validate = (
  operation: BulkModificationListStringProcessOperation,
  options?: EditingListStringValueProcessOptions,
): string | undefined => {
  if (operation == BulkModificationListStringProcessOperation.Delete) {
    return;
  }

  if (!options) {
    return "bulkModification.validation.optionsRequired";
  }

  const { value, valueType, isOperationDirectionReversed, modifyOptions } =
    options;

  switch (operation) {
    case BulkModificationListStringProcessOperation.SetWithFixedValue:
    case BulkModificationListStringProcessOperation.Append:
    case BulkModificationListStringProcessOperation.Prepend:
      if (!value) {
        return "bulkModification.validation.valueRequired";
      }
      break;
    case BulkModificationListStringProcessOperation.Modify:
      if (modifyOptions == undefined) {
        return "bulkModification.validation.modifyOptionsRequired";
      }

      if (
        modifyOptions.filterBy !=
          BulkModificationProcessorOptionsItemsFilterBy.All &&
        (modifyOptions.filterValue == undefined ||
          modifyOptions.filterValue.length == 0)
      ) {
        return "bulkModification.validation.filterValueRequired";
      }

      if (modifyOptions.operation == undefined) {
        return "bulkModification.validation.operationRequired";
      }

      return validateStringValueProcessOptions(
        modifyOptions.operation,
        modifyOptions.options,
      );
  }

  return;
};
