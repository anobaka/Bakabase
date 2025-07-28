"use client";

"use strict";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BulkModificationVariable } from "@/pages/bulk-modification2/components/BulkModification/models";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { StringValueProcessEditor } from "../Processes/StringValueProcess";
import { ListStringValueProcessEditor } from "../Processes/ListStringValueProcess";

import { Modal } from "@/components/bakaui";
import { BulkModificationProcessorValueType } from "@/sdk/constants";
import { PropertyType } from "@/sdk/constants";
import { buildLogger } from "@/components/utils";

type Props = {
  property: IProperty;
  operation?: number;
  options?: any;
  onSubmit?: (operation: number, options: any) => any;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
} & DestroyableProps;

const log = buildLogger("ProcessStepModal");
const ProcessStepModal = ({
  property,
  operation: propsOperation,
  options: propsOptions,
  onDestroyed,
  onSubmit,
  variables,
  availableValueTypes = [BulkModificationProcessorValueType.ManuallyInput],
}: Props) => {
  const { t } = useTranslation();

  const [operation, setOperation] = useState<number | undefined>(
    propsOperation,
  );
  const [options, setOptions] = useState<any>(propsOptions);
  const [error, setError] = useState<string | undefined>();

  log("property", property, "operation", operation, "options", options);

  const renderOptions = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <StringValueProcessEditor
            availableValueTypes={availableValueTypes}
            operation={operation}
            options={options}
            propertyType={property.type}
            variables={variables}
            onChange={(operation, options, error) => {
              setOperation(operation);
              setOptions(options);
              setError(error);
            }}
          />
        );
      case PropertyType.MultipleChoice:
      case PropertyType.Attachment:
        return (
          <ListStringValueProcessEditor
            availableValueTypes={availableValueTypes}
            operation={operation}
            options={options}
            property={property}
            variables={variables}
            onChange={(operation, options, error) => {
              setOperation(operation);
              setOptions(options);
              setError(error);
            }}
          />
        );
      case PropertyType.Number:
      case PropertyType.Percentage:
      case PropertyType.Rating:
        break;
      case PropertyType.Boolean:
        break;
      case PropertyType.Link:
        break;
      case PropertyType.Date:
      case PropertyType.DateTime:
      case PropertyType.Time:
        break;
      case PropertyType.Multilevel:
        break;
      case PropertyType.Tags:
        break;
    }

    return t<string>("Not supported");
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !!error,
        },
      }}
      size={"xl"}
      onDestroyed={onDestroyed}
      onOk={() => {
        if (operation != undefined) {
          log("onSubmit", "operation", operation, "options", options);
          onSubmit?.(operation!, options);
        }
      }}
    >
      {renderOptions()}
      {error && (
        <div className={"whitespace-break-spaces text-danger"}>
          {t<string>("ERROR")}: {error}
        </div>
      )}
    </Modal>
  );
};

ProcessStepModal.displayName = "ProcessStepModal";

export default ProcessStepModal;
