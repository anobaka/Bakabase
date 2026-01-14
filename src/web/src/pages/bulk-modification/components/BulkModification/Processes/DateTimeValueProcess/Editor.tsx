"use client";

import type { DateTimeProcessOptions } from "./models";
import type { PropertyType } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationDateTimeProcessOperation,
  bulkModificationDateTimeProcessOperations,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { NumberInput, Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationDateTimeProcessOperation;
  options?: DateTimeProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationDateTimeProcessOperation,
    options?: DateTimeProcessOptions,
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
  const [options, setOptions] = useState<DateTimeProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationDateTimeProcessOperation>(
    propsOperation ?? BulkModificationDateTimeProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<DateTimeProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationDateTimeProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationDateTimeProcessOperation.Delete:
      case BulkModificationDateTimeProcessOperation.SetToNow:
        return null;
      case BulkModificationDateTimeProcessOperation.SetWithFixedValue:
        return (
          <ProcessValueEditor
            availableValueTypes={availableValueTypes}
            baseValueType={propertyType}
            value={options.value}
            variables={variables}
            onChange={(value) => changeOptions({ value })}
          />
        );
      case BulkModificationDateTimeProcessOperation.AddDays:
      case BulkModificationDateTimeProcessOperation.SubtractDays:
      case BulkModificationDateTimeProcessOperation.AddMonths:
      case BulkModificationDateTimeProcessOperation.SubtractMonths:
      case BulkModificationDateTimeProcessOperation.AddYears:
      case BulkModificationDateTimeProcessOperation.SubtractYears:
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
        dataSource={bulkModificationDateTimeProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationDateTimeProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationDateTimeProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "DateTimeValueProcessEditor";

export default Editor;
