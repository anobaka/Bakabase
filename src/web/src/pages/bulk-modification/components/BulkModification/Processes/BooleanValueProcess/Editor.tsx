"use client";

import type { BooleanProcessOptions } from "./models";
import type { PropertyType } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationBooleanProcessOperation,
  bulkModificationBooleanProcessOperations,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Select } from "@/components/bakaui";

type Props = {
  operation?: BulkModificationBooleanProcessOperation;
  options?: BooleanProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationBooleanProcessOperation,
    options?: BooleanProcessOptions,
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
  const [options, setOptions] = useState<BooleanProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationBooleanProcessOperation>(
    propsOperation ?? BulkModificationBooleanProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<BooleanProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationBooleanProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationBooleanProcessOperation.Delete:
      case BulkModificationBooleanProcessOperation.Toggle:
        return null;
      case BulkModificationBooleanProcessOperation.SetWithFixedValue:
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
        dataSource={bulkModificationBooleanProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationBooleanProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationBooleanProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "BooleanValueProcessEditor";

export default Editor;
