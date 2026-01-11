import type { IProperty } from "@/components/Property/models";
import type { PropertyType, StandardValueType } from "@/sdk/constants";
import type { BulkModificationProcessValue } from "@/pages/bulk-modification/components/BulkModification/models";

import { BulkModificationProcessorValueType } from "@/sdk/constants";

export const buildFakeProperty = (
  type: PropertyType,
  dbValueType: StandardValueType,
  bizValueType: StandardValueType,
): IProperty => {
  return {
    id: 0,
    name: "",
    type: type,
    typeName: "",
    pool: 0,
    poolName: "",
    dbValueType: dbValueType,
    bizValueType: bizValueType,
  };
};

export const validate = (value?: Partial<BulkModificationProcessValue>): string | undefined => {
  if (!value) {
    return "bulkModification.validation.invalidValue";
  }

  if (value.type == undefined) {
    return "bulkModification.validation.invalidValueType";
  }

  switch (value.type) {
    case BulkModificationProcessorValueType.ManuallyInput: {
      if (value.editorPropertyType == undefined) {
        return "bulkModification.validation.invalidPropertyType";
      }
      if (value.value == undefined || value.value.length == 0) {
        return "bulkModification.validation.invalidValue";
      }
      break;
    }
    case BulkModificationProcessorValueType.Variable:
      if (value.value == undefined) {
        return "bulkModification.validation.invalidVariable";
      }
      break;
  }

  return;
};
