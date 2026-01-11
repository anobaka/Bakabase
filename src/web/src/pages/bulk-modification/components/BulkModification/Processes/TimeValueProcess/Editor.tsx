"use client";

import type { TimeProcessOptions } from "./models";
import type { PropertyType } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationTimeProcessOperation,
  bulkModificationTimeProcessOperations,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { NumberInput, Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationTimeProcessOperation;
  options?: TimeProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationTimeProcessOperation,
    options?: TimeProcessOptions,
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
  const [options, setOptions] = useState<TimeProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationTimeProcessOperation>(
    propsOperation ?? BulkModificationTimeProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<TimeProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationTimeProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationTimeProcessOperation.Delete:
        return null;
      case BulkModificationTimeProcessOperation.SetWithFixedValue:
        return (
          <ProcessValueEditor
            availableValueTypes={availableValueTypes}
            baseValueType={propertyType}
            value={options.value}
            variables={variables}
            onChange={(value) => changeOptions({ value })}
          />
        );
      case BulkModificationTimeProcessOperation.AddHours:
      case BulkModificationTimeProcessOperation.SubtractHours:
      case BulkModificationTimeProcessOperation.AddMinutes:
      case BulkModificationTimeProcessOperation.SubtractMinutes:
      case BulkModificationTimeProcessOperation.AddSeconds:
      case BulkModificationTimeProcessOperation.SubtractSeconds:
        return (
          <NumberInput
            label={t("bulkModification.label.amount")}
            min={0}
            value={options.amount}
            onValueChange={(amount) => changeOptions({ amount })}
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
        dataSource={bulkModificationTimeProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationTimeProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationTimeProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "TimeValueProcessEditor";

export default Editor;
