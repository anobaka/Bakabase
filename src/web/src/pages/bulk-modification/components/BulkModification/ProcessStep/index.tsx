"use client";

"use strict";
import type { IProperty } from "@/components/Property/models";
import type {
  BulkModificationProcessStep,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";
import { DeleteOutlined } from "@ant-design/icons";
import { useUpdateEffect } from "react-use";

import { StringValueProcessDemonstrator } from "../Processes/StringValueProcess";
import { ListStringValueProcessDemonstrator } from "../Processes/ListStringValueProcess";

import { Button, Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessStepModal from "@/pages/bulk-modification/components/BulkModification/ProcessStepModal";
import { type BulkModificationProcessorValueType, PropertyType } from "@/sdk/constants";

type Props = {
  no: any;
  step: BulkModificationProcessStep;
  property: IProperty;
  onChange?: (step: BulkModificationProcessStep) => any;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  editable?: boolean;
  onDelete: () => any;
};
const ProcessStep = ({
  no,
  step: propsStep,
  property,
  onChange,
  variables,
  editable,
  availableValueTypes,
  onDelete,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [step, setStep] = useState<BulkModificationProcessStep>(propsStep);

  useUpdateEffect(() => {
    setStep(propsStep);
  }, [propsStep]);

  const renderDemonstrator = () => {
    switch (property.type) {
      case PropertyType.SingleLineText:
      case PropertyType.MultilineText:
      case PropertyType.Formula:
      case PropertyType.SingleChoice:
        return (
          <StringValueProcessDemonstrator
            operation={step.operation}
            options={step.options}
            variables={variables}
          />
        );
      case PropertyType.MultipleChoice:
      case PropertyType.Attachment:
        return (
          <ListStringValueProcessDemonstrator
            operation={step.operation}
            options={step.options}
            property={property}
            variables={variables}
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
    <div
      className={`flex items-center flex-wrap gap-1 ${editable ? "cursor-pointer hover:bg-[var(--bakaui-overlap-background)] rounded" : ""}`}
      onClick={
        editable
          ? () => {
              createPortal(ProcessStepModal, {
                property: property,
                availableValueTypes,
                onSubmit: (operation, options) => {
                  const newStep = {
                    ...step,
                    operation,
                    options,
                  };

                  setStep(newStep);
                  onChange?.(newStep);
                },
                variables: variables,
                operation: step.operation,
                options: step.options,
              });
            }
          : undefined
      }
    >
      <Chip radius={"sm"} size={"sm"}>
        {no}
      </Chip>
      {renderDemonstrator()}
      <Button
        className={"min-w-fit px-2"}
        color={"danger"}
        size={"sm"}
        variant={"light"}
        onClick={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("Delete a process step"),
            children: t<string>("Are you sure you want to delete this process step?"),
            onOk: async () => {
              onDelete();
            },
          });
        }}
      >
        <DeleteOutlined className={"text-base"} />
      </Button>
    </div>
  );
};

ProcessStep.displayName = "ProcessStep";

export default ProcessStep;
