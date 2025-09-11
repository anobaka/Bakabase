"use client";

import type { ResourceSearchFilter } from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import PropertySelector from "@/components/PropertySelector";
import { Button } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";
const PropertySelectorTest = () => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<ResourceSearchFilter>({});
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      text
      size={"small"}
      type={"primary"}
      onClick={() => {
        createPortal(PropertySelector, {
          selection: {
            [filter.propertyPool == PropertyPool.Custom
              ? "reservedPropertyIds"
              : "customPropertyIds"]:
              filter.propertyId == undefined ? undefined : [filter.propertyId],
          },
          onSubmit: async (selectedProperties) => {
            const property = (selectedProperties.reservedProperties?.[0] ??
              selectedProperties.customProperties?.[0])!;
            const cp = property as ICustomProperty;

            setFilter({
              ...filter,
              propertyId: property.id,
              propertyName: property.name,
              propertyPool: cp == undefined,
            });
          },
          multiple: false,
          pool: "all",
        });
      }}
    >
      {filter.propertyId ? filter.propertyName : t<string>("Property")}
    </Button>
  );
};

PropertySelectorTest.displayName = "PropertySelectorTest";

export default PropertySelectorTest;
