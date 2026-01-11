"use client";

import type { ListListStringProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationListListStringProcessOperation,
  bulkModificationListListStringProcessOperations,
  PropertyType,
} from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationListListStringProcessOperation;
  options?: ListListStringProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationListListStringProcessOperation,
    options?: ListListStringProcessOptions,
    error?: string,
  ) => any;
  propertyType: PropertyType;
};

const Editor = ({
  operation: propsOperation,
  options: propsOptions,
  onChange,
  variables,
  availableValueTypes,
  propertyType,
}: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<ListListStringProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationListListStringProcessOperation>(
    propsOperation ?? BulkModificationListListStringProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<ListListStringProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationListListStringProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationListListStringProcessOperation.Delete:
        return null;
      case BulkModificationListListStringProcessOperation.SetWithFixedValue:
      case BulkModificationListListStringProcessOperation.Append:
      case BulkModificationListListStringProcessOperation.Prepend:
      case BulkModificationListListStringProcessOperation.Remove:
        return (
          <ProcessValueEditor
            availableValueTypes={availableValueTypes}
            baseValueType={propertyType}
            value={options.value}
            variables={variables}
            onChange={(value) => changeOptions({ value })}
          />
        );
      default:
        return null;
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <Select
        label={t("bulkModification.label.operation")}
        dataSource={bulkModificationListListStringProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationListListStringProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationListListStringProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "ListListStringValueProcessEditor";

export default Editor;
