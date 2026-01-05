import type { LinkProcessOptions } from "./models";

import { BulkModificationLinkProcessOperation } from "@/sdk/constants";
import { validate as validateStringProcessOptions } from "../StringValueProcess/helpers";

export const validate = (
  operation: BulkModificationLinkProcessOperation,
  options: LinkProcessOptions,
): string | undefined => {
  switch (operation) {
    case BulkModificationLinkProcessOperation.Delete:
      return undefined;
    case BulkModificationLinkProcessOperation.SetWithFixedValue:
      if (!options.value) {
        return "bulkModification.validation.valueRequired";
      }
      return undefined;
    case BulkModificationLinkProcessOperation.SetText:
      if (!options.text) {
        return "bulkModification.validation.textRequired";
      }
      return undefined;
    case BulkModificationLinkProcessOperation.SetUrl:
      if (!options.url) {
        return "bulkModification.validation.urlRequired";
      }
      return undefined;
    case BulkModificationLinkProcessOperation.ModifyText:
    case BulkModificationLinkProcessOperation.ModifyUrl:
      if (options.stringOperation === undefined) {
        return "bulkModification.validation.stringOperationRequired";
      }
      return validateStringProcessOptions(options.stringOperation, options.stringOptions ?? {});
    default:
      return undefined;
  }
};
