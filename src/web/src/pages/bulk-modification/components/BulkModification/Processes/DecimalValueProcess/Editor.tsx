"use client";

import type { DecimalProcessOptions } from "./models";
import type { PropertyType } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationDecimalProcessOperation,
  bulkModificationDecimalProcessOperations,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { NumberInput, Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationDecimalProcessOperation;
  options?: DecimalProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationDecimalProcessOperation,
    options?: DecimalProcessOptions,
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
  const [options, setOptions] = useState<DecimalProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationDecimalProcessOperation>(
    propsOperation ?? BulkModificationDecimalProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<DecimalProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationDecimalProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationDecimalProcessOperation.Delete:
      case BulkModificationDecimalProcessOperation.Ceil:
      case BulkModificationDecimalProcessOperation.Floor:
        return null;
      case BulkModificationDecimalProcessOperation.SetWithFixedValue:
      case BulkModificationDecimalProcessOperation.Add:
      case BulkModificationDecimalProcessOperation.Subtract:
      case BulkModificationDecimalProcessOperation.Multiply:
      case BulkModificationDecimalProcessOperation.Divide:
        return (
          <>
            <div>{t("bulkModification.label.value")}</div>
            <ProcessValueEditor
              availableValueTypes={availableValueTypes}
              baseValueType={propertyType}
              value={options.value}
              variables={variables}
              onChange={(value) => changeOptions({ value })}
            />
          </>
        );
      case BulkModificationDecimalProcessOperation.Round:
        return (
          <>
            <div>{t("bulkModification.label.decimalPlaces")}</div>
            <NumberInput
              min={0}
              value={options.decimalPlaces}
              onValueChange={(decimalPlaces) => changeOptions({ decimalPlaces })}
            />
          </>
        );
      default:
        return null;
    }
  };

  return (
    <div
      className="grid items-center gap-2"
      style={{ gridTemplateColumns: "auto minmax(0, 1fr)" }}
    >
      <div>{t("bulkModification.label.operation")}</div>
      <Select
        dataSource={bulkModificationDecimalProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationDecimalProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationDecimalProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "DecimalValueProcessEditor";

export default Editor;
