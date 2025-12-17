"use client";

import type { SearchFilter } from "@/components/ResourceFilter";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import PropertySelector from "@/components/PropertySelector";
import { Button } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";

const PropertySelectorTest = () => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<SearchFilter>({ disabled: false });
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      color={"primary"}
      size={"sm"}
      variant={"light"}
      onClick={() => {
        createPortal(PropertySelector, {
          v2: true,
          selection:
            filter.propertyId == undefined
              ? undefined
              : [{ id: filter.propertyId, pool: filter.propertyPool! }],
          onSubmit: async (selectedProperties) => {
            const property = selectedProperties[0];

            if (property) {
              setFilter({
                ...filter,
                propertyId: property.id,
                propertyPool: property.pool,
                property,
              });
            }
          },
          multiple: false,
          pool: PropertyPool.All,
        });
      }}
    >
      {filter.property?.name ?? t<string>("Property")}
    </Button>
  );
};

PropertySelectorTest.displayName = "PropertySelectorTest";

export default PropertySelectorTest;
