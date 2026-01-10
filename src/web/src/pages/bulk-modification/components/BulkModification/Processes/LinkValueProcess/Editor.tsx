"use client";

import type { LinkProcessOptions } from "./models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationLinkProcessOperation,
  bulkModificationLinkProcessOperations,
  PropertyType,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";
import { Select } from "@/components/bakaui";
import { StringValueProcessEditor } from "../StringValueProcess";
import { getEnumKey } from "@/i18n";

type Props = {
  operation?: BulkModificationLinkProcessOperation;
  options?: LinkProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationLinkProcessOperation,
    options?: LinkProcessOptions,
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
  const [options, setOptions] = useState<LinkProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationLinkProcessOperation>(
    propsOperation ?? BulkModificationLinkProcessOperation.SetWithFixedValue,
  );

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options, error == undefined ? undefined : t<string>(error));
  }, [options, operation]);

  const changeOptions = (patches: Partial<LinkProcessOptions>) => {
    setOptions({ ...options, ...patches });
  };

  const changeOperation = (newOperation: BulkModificationLinkProcessOperation) => {
    setOperation(newOperation);
    setOptions({});
  };

  const renderSubOptions = () => {
    switch (operation) {
      case BulkModificationLinkProcessOperation.Delete:
        return null;
      case BulkModificationLinkProcessOperation.SetWithFixedValue:
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
      case BulkModificationLinkProcessOperation.SetText:
        return (
          <>
            <div>{t("bulkModification.label.text")}</div>
            <ProcessValueEditor
              availableValueTypes={availableValueTypes}
              baseValueType={PropertyType.SingleLineText}
              value={options.text}
              variables={variables}
              onChange={(text) => changeOptions({ text })}
            />
          </>
        );
      case BulkModificationLinkProcessOperation.SetUrl:
        return (
          <>
            <div>{t("bulkModification.label.url")}</div>
            <ProcessValueEditor
              availableValueTypes={availableValueTypes}
              baseValueType={PropertyType.SingleLineText}
              value={options.url}
              variables={variables}
              onChange={(url) => changeOptions({ url })}
            />
          </>
        );
      case BulkModificationLinkProcessOperation.ModifyText:
      case BulkModificationLinkProcessOperation.ModifyUrl:
        return (
          <>
            <div>{t("bulkModification.label.stringOperation")}</div>
            <StringValueProcessEditor
              availableValueTypes={availableValueTypes}
              operation={options.stringOperation}
              options={options.stringOptions}
              propertyType={PropertyType.SingleLineText}
              variables={variables}
              onChange={(stringOperation, stringOptions) => {
                changeOptions({ stringOperation, stringOptions });
              }}
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
        dataSource={bulkModificationLinkProcessOperations.map((op) => ({
          label: t(getEnumKey('BulkModificationLinkProcessOperation', op.label)),
          value: op.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode="single"
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationLinkProcessOperation,
          );
        }}
      />
      {renderSubOptions()}
    </div>
  );
};

Editor.displayName = "LinkValueProcessEditor";

export default Editor;
