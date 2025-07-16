"use client";

import type { ResourceSearchFilter } from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import PropertySelector from "@/components/PropertySelector";
import { Button } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";

export default () => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<ResourceSearchFilter>({});

  return (
    <Button
      text
      size={"small"}
      type={"primary"}
      onClick={() => {
        PropertySelector.show({
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
