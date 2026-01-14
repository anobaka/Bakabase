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
          <ProcessValueEditor
            availableValueTypes={availableValueTypes}
            baseValueType={propertyType}
            value={options.value}
            variables={variables}
            onChange={(value) => changeOptions({ value })}
          />
        );
      case BulkModificationLinkProcessOperation.SetText:
        return (
          <div className="flex flex-col gap-1">
            <span className="text-sm text-default-500">{t("bulkModification.label.text")}</span>
            <ProcessValueEditor
              availableValueTypes={availableValueTypes}
              baseValueType={PropertyType.SingleLineText}
              value={options.text}
              variables={variables}
              onChange={(text) => changeOptions({ text })}
            />
          </div>
        );
      case BulkModificationLinkProcessOperation.SetUrl:
        return (
          <div className="flex flex-col gap-1">
            <span className="text-sm text-default-500">{t("bulkModification.label.url")}</span>
            <ProcessValueEditor
              availableValueTypes={availableValueTypes}
              baseValueType={PropertyType.SingleLineText}
              value={options.url}
              variables={variables}
              onChange={(url) => changeOptions({ url })}
            />
          </div>
        );
      case BulkModificationLinkProcessOperation.ModifyText:
      case BulkModificationLinkProcessOperation.ModifyUrl:
        return (
          <div className="flex flex-col gap-1">
            <span className="text-sm text-default-500">{t("bulkModification.label.stringOperation")}</span>
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
          </div>
        );
      default:
        return null;
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <Select
        label={t("bulkModification.label.operation")}
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
