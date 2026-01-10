"use client";

import type { IProperty } from "@/components/Property/models";
import type {
  BulkModificationProcessStep,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React from "react";

import { StringValueProcessDemonstrator } from "../Processes/StringValueProcess";
import { ListStringValueProcessDemonstrator } from "../Processes/ListStringValueProcess";
import { DecimalValueProcessDemonstrator } from "../Processes/DecimalValueProcess";
import { BooleanValueProcessDemonstrator } from "../Processes/BooleanValueProcess";
import { DateTimeValueProcessDemonstrator } from "../Processes/DateTimeValueProcess";
import { TimeValueProcessDemonstrator } from "../Processes/TimeValueProcess";
import { LinkValueProcessDemonstrator } from "../Processes/LinkValueProcess";
import { ListListStringValueProcessDemonstrator } from "../Processes/ListListStringValueProcess";
import { ListTagValueProcessDemonstrator } from "../Processes/ListTagValueProcess";

import { PropertyType } from "@/sdk/constants";

type Props = {
  property: IProperty;
  step: BulkModificationProcessStep;
  variables?: BulkModificationVariable[];
};

const StepDemonstrator = ({ property, step, variables }: Props) => {
  const { t } = useTranslation();

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
      return (
        <DecimalValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Boolean:
      return (
        <BooleanValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Link:
      return (
        <LinkValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Date:
    case PropertyType.DateTime:
      return (
        <DateTimeValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Time:
      return (
        <TimeValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Multilevel:
      return (
        <ListListStringValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    case PropertyType.Tags:
      return (
        <ListTagValueProcessDemonstrator
          operation={step.operation}
          options={step.options}
          variables={variables}
        />
      );
    default:
      return <>{t<string>("bulkModification.empty.notSupported")}</>;
  }
};

StepDemonstrator.displayName = "StepDemonstrator";

export default StepDemonstrator;
