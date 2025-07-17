"use client";

import type { PropertyType, StandardValueType } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useState } from "react";

import PropertySelector from "@/components/PropertySelector";
import { Button } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";

interface IProps {
  onSelected?: (property: IProperty) => any;
  valueTypes?: PropertyType[];
}

export default (props: IProps) => {
  const { t } = useTranslation();

  const [property, setProperty] = useState<IProperty>();

  return (
    <Button
      className={"ml-2"}
      color={"primary"}
      size={"sm"}
      variant={"light"}
      onClick={() => {
        PropertySelector.show({
          editable: true,
          removable: true,
          addable: true,
          pool: PropertyPool.Custom | PropertyPool.Reserved,
          multiple: false,
          valueTypes: props.valueTypes?.map(
            (v) => v as unknown as StandardValueType,
          ),
          onSubmit: async (selected) => {
            const p = selected![0];

            setProperty(p);
            props.onSelected?.(p);
          },
        });
      }}
    >
      {property ? property.name : t<string>("Select a property")}
    </Button>
  );
};
