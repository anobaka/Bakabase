"use client";

import type { PropertyPool, PropertyValueScope } from "@/sdk/constants";
import type { Property, PropertyValueScopePreference } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";

import React from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";

import { propertyValueScopes } from "@/sdk/constants";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";
import {
  convertFromApiValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

import ScopePreferencePopover from "./ScopePreferencePopover";

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
  resourceId?: number;
  propertyPool?: PropertyPool;
  scopePreference?: PropertyValueScopePreference;
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
    resourceId,
    propertyPool,
    scopePreference,
  } = props;
  const { t } = useTranslation();

  const [isLinked, setIsLinked] = React.useState(propsIsLinked);
  const [isScopePopoverOpen, setIsScopePopoverOpen] = React.useState(false);

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

  const canShowPopover =
    !hidePropertyName && resourceId !== undefined && propertyPool !== undefined;

  const titleNode = (
    <div className={`flex items-center gap-1 ${classNames?.name ?? ""}`}>
      <BriefProperty
        chipProps={
          layout === "vertical"
            ? {
                variant: "light",
                className: "px-0 leading-none",
                classNames: { content: "px-0", base: "h-auto" },
              }
            : undefined
        }
        fields={["pool", "name"]}
        property={property}
        showPoolChip={false}
      />
      {canShowPopover && (
        <span
          className={`transition-opacity duration-150 ${
            isScopePopoverOpen ? "opacity-100" : "opacity-0 group-hover:opacity-100 focus-within:opacity-100"
          }`}
        >
          <ScopePreferencePopover
            effectivePriority={valueScopePriority}
            preference={scopePreference}
            propertyId={property.id}
            propertyPool={propertyPool!}
            resourceId={resourceId!}
            values={values}
            onChanged={() => onValueScopePriorityChange(valueScopePriority)}
            onOpenChange={setIsScopePopoverOpen}
          />
        </span>
      )}
    </div>
  );

  if (layout === "vertical") {
    return (
      <div className="flex flex-col gap-0.5 group">
        {!hidePropertyName && titleNode}
        <div className={`flex items-center gap-2 break-all ${classNames?.value}`}>
          <PropertyValueRenderer
            bizValue={serializeStandardValue(
              convertFromApiValue(bizValue, property.bizValueType),
              property.bizValueType,
            )}
            dbValue={serializeStandardValue(
              convertFromApiValue(dbValue, property.dbValueType),
              property.dbValueType,
            )}
            property={property}
            variant={"default"}
            onValueChange={onValueChange}
          />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-0.5 group">
      {!hidePropertyName && titleNode}
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
