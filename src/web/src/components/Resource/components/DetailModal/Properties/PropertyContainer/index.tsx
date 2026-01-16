"use client";

import type { PropertyValueScope } from "@/sdk/constants";
import type { Property } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";

import React from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";

import { propertyValueScopes } from "@/sdk/constants";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

export type PropertyContainerProps = {
  valueScopePriority: PropertyValueScope[];
  onValueScopePriorityChange: (priority: PropertyValueScope[]) => any;
  property: IProperty;
  values?: Property["values"];
  onValueChange: (sdv?: string, sbv?: string) => any;
  hidePropertyName?: boolean;
  classNames?: {
    name?: string;
    value?: string;
  };
  isLinked?: boolean;
  categoryId: number;
  layout?: "horizontal" | "vertical";
};

const log = buildLogger("PropertyContainer");
const PropertyContainer = (props: PropertyContainerProps) => {
  const forceUpdate = useUpdate();

  log(props);
  const {
    valueScopePriority,
    onValueScopePriorityChange,
    values,
    property,
    onValueChange,
    hidePropertyName = false,
    classNames,
    isLinked: propsIsLinked,
    categoryId,
    layout = "horizontal",
  } = props;
  const { t } = useTranslation();

  const [isLinked, setIsLinked] = React.useState(propsIsLinked);

  const scopedValueCandidates = propertyValueScopes.map((s) => {
    return {
      key: s.value,
      scope: s.value,
      value: values?.find((x) => x.scope == s.value),
    };
  });

  let bizValue: any | undefined;
  let dbValue: any | undefined;
  let scope: PropertyValueScope | undefined;

  for (const s of valueScopePriority) {
    const value = values?.find((v) => v.scope == s);

    if (value) {
      const dv = value.aliasAppliedBizValue ?? value.bizValue;

      if (dv) {
        bizValue = dv;
        dbValue = value.value;
        scope = s;
        break;
      }
    }
  }
  scope ??= valueScopePriority[0];

  if (layout === "vertical") {
    return (
      <div className="flex flex-col gap-0.5">
        {!hidePropertyName && (
          <div className={classNames?.name}>
            <BriefProperty
              chipProps={{ variant: "light", className: "px-0 leading-none", classNames: { content: "px-0", base: "h-auto"} }}
              fields={["pool", "name"]}
              property={property}
              showPoolChip={false}
            />
          </div>
        )}
        <div className={`flex items-center gap-2 break-all ${classNames?.value}`}>
          <PropertyValueRenderer
            bizValue={serializeStandardValue(bizValue, property.bizValueType)}
            dbValue={serializeStandardValue(dbValue, property.dbValueType)}
            property={property}
            variant={"default"}
            onValueChange={onValueChange}
          />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-0.5">
      {!hidePropertyName && (
        <div className={classNames?.name}>
          <BriefProperty fields={["pool", "name"]} property={property} showPoolChip={false} />
        </div>
      )}
      <div className={`flex items-center gap-2 break-all ${classNames?.value}`}>
        <PropertyValueRenderer
          bizValue={serializeStandardValue(bizValue, property.bizValueType)}
          dbValue={serializeStandardValue(dbValue, property.dbValueType)}
          property={property}
          variant={"default"}
          onValueChange={onValueChange}
        />
      </div>
    </div>
  );
};

PropertyContainer.displayName = "PropertyContainer";

export default PropertyContainer;
