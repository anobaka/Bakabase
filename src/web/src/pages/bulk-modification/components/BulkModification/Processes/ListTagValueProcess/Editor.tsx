"use client";

import type { ListTagProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationListTagProcessOperation,
  bulkModificationListTagProcessOperations,
  PropertyType,
} from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationListTagProcessOperation;
  options?: ListTagProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationListTagProcessOperation,
    options?: ListTagProcessOptions,
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
  const [options, setOptions] = useState<ListTagProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationListTagProcessOperation>(
    propsOperation ?? BulkModificationListTagProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<ListTagProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationListTagProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationListTagProcessOperation.Delete:
        return null;
      case BulkModificationListTagProcessOperation.SetWithFixedValue:
      case BulkModificationListTagProcessOperation.Append:
      case BulkModificationListTagProcessOperation.Prepend:
      case BulkModificationListTagProcessOperation.Remove:
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
        dataSource={bulkModificationListTagProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationListTagProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationListTagProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "ListTagValueProcessEditor";

export default Editor;
