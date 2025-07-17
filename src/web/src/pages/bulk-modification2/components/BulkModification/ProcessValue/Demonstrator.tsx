"use client";

"use strict";
import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from "@/pages/bulk-modification2/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React from "react";

import { BulkModificationProcessorValueType } from "@/sdk/constants";
import { Chip } from "@/components/bakaui";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";

type Props = {
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
};

const log = buildLogger("BulkModificationProcessValueDemonstrator");

export default ({ variables, value }: Props) => {
  const { t } = useTranslation();

  log(value, variables);

  if (!value) {
    return (
      <Chip
        isDisabled
        size={'sm'}
        // color={'danger'}
        radius={'sm'}
      >
        {t<string>("No set")}
      </Chip>
    );
  }

  switch (value.type) {
    case BulkModificationProcessorValueType.ManuallyInput:
      if (!value.property) {
        return (
          <Chip isDisabled color={"danger"} radius={"sm"} size={"sm"}>
            {t<string>("Unable to get property information")}
          </Chip>
        );
      }

      return (
        <PropertyValueRenderer
          bizValue={value.followPropertyChanges ? undefined : value.value}
          dbValue={value.followPropertyChanges ? value.value : undefined}
          property={value.property}
          // variant={'light'}
        />
      );
    case BulkModificationProcessorValueType.Variable:
      return (
        <div className={"flex items-center gap-1"}>
          <Chip color={"secondary"} radius={"sm"} size={"sm"} variant={"flat"}>
            {t<string>(
              `BulkModificationProcessorValueType.${BulkModificationProcessorValueType[value.type]}`,
            )}
          </Chip>
          <Chip radius={"sm"} size={"sm"}>
            {variables?.find((v) => v.key === value.value)?.name}
          </Chip>
        </div>
      );
  }
};
