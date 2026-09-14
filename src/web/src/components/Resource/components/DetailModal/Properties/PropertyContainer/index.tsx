"use client";

import type { PropertyPool, PropertyValueScope } from "@/sdk/constants";
import type { Property, PropertyValueScopePreference } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";

import React from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";
import { AiOutlinePlusCircle } from "react-icons/ai";

import ScopePreferencePopover from "./ScopePreferencePopover";

import BApi from "@/sdk/BApi";
import { propertyValueScopes, PropertyType } from "@/sdk/constants";
import { selectScopedValue } from "@/core/models/Resource";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";
import { convertFromApiValue, serializeStandardValue } from "@/components/StandardValue/helpers";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import { Tooltip } from "@/components/bakaui";

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

  const selectedValue = selectScopedValue(values, valueScopePriority);
  const bizValue = selectedValue?.aliasAppliedBizValue ?? selectedValue?.bizValue;
  const dbValue = selectedValue?.value;

  const canShowPopover =
    !hidePropertyName && resourceId !== undefined && propertyPool !== undefined;
  const canBindToProfiles =
    !hidePropertyName && !isLinked && resourceId !== undefined && propertyPool !== undefined;

  const handleBindToProfiles = async () => {
    if (resourceId === undefined || propertyPool === undefined) return;
    await BApi.resourceProfile.bindPropertyToMatchingProfiles(resourceId, {
      pool: propertyPool,
      id: property.id,
    });
    onValueScopePriorityChange(valueScopePriority);
  };

  const titleNode = (
    <div className={`flex min-w-0 items-start gap-2 ${classNames?.name ?? ""}`}>
      <div className="min-w-0">
        <BriefProperty
          chipProps={
            layout === "vertical"
              ? {
                  variant: "light",
                  color: "default",
                  className: "h-auto min-w-0 max-w-full px-0",
                  classNames: {
                    content:
                      "min-w-0 whitespace-normal px-0 text-xs font-medium leading-5 text-default-500 [overflow-wrap:anywhere]",
                    base: "h-auto min-w-0 max-w-full",
                  },
                }
              : undefined
          }
          fields={["pool", "name"]}
          property={property}
          showPoolChip={false}
        />
      </div>
      <div className="flex shrink-0 items-center gap-1">
        {canBindToProfiles && (
          <Tooltip color={"foreground"} content={t("property.bindToProfiles.title")}>
            <button
              aria-label={t<string>("property.bindToProfiles.title")}
              className="inline-flex h-5 w-5 items-center justify-center rounded text-default-400 transition-colors hover:bg-default-100 hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
              type="button"
              onClick={handleBindToProfiles}
            >
              <AiOutlinePlusCircle size={14} />
            </button>
          </Tooltip>
        )}
        {canShowPopover && (
          <span
            className={`transition-opacity duration-150 ${
              isScopePopoverOpen
                ? "opacity-100"
                : "opacity-40 group-hover/property:opacity-100 group-focus-within/property:opacity-100"
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
    </div>
  );

  if (layout === "vertical") {
    return (
      <div
        className={`group group/property flex min-w-0 flex-col gap-1.5 rounded-lg transition-colors ${hidePropertyName ? "" : "-mx-2 px-2 py-2 hover:bg-default-50/60 focus-within:bg-default-50/60"}`}
      >
        {!hidePropertyName && titleNode}
        <div
          className={`flex min-w-0 max-w-full items-center gap-2 text-sm leading-relaxed text-default-700 [overflow-wrap:anywhere] [&>*]:min-w-0 [&>*]:max-w-full ${classNames?.value ?? ""}`}
        >
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
            size={
              hidePropertyName ||
              property.type === PropertyType.Attachment ||
              property.type === PropertyType.Rating
                ? "md"
                : "sm"
            }
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
