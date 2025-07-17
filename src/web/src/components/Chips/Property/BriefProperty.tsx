"use client";

import type { PropertyPool, PropertyType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import PropertyPoolIcon from "@/components/Property/components/PropertyPoolIcon";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { Chip } from "@/components/bakaui";

type Property = {
  name: string;
  pool?: PropertyPool;
  type?: PropertyType;
};

type Field = "pool" | "type" | "name";

type Props = {
  property: Property;

  fields?: Field[];
};

export default ({ property, fields }: Props) => {
  const { t } = useTranslation();

  fields ??= ["pool", "type", "name"];

  return (
    <div className="flex items-center gap-1">
      {property
        ? fields.map((f) => {
            switch (f) {
              case "pool":
                return <PropertyPoolIcon pool={property.pool} />;
              case "type":
                return <PropertyTypeIcon type={property.type} />;
              case "name":
                return (
                  <Chip radius={"sm"} size="sm" variant={"flat"}>
                    {property.name}
                  </Chip>
                );
            }
          })
        : t<string>("Unknown property")}
    </div>
  );
};
