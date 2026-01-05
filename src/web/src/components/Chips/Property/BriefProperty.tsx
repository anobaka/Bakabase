"use client";

import type { PropertyPool, PropertyType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import PropertyPoolIcon, { getPropertyPoolColor } from "@/components/Property/components/PropertyPoolIcon";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { Chip, type ChipProps } from "@/components/bakaui";

type Property = {
  name: string;
  pool?: PropertyPool;
  type?: PropertyType;
};

type Field = "pool" | "type" | "name";

type Props = {
  property: Property;
  fields?: Field[];
  showPoolChip?: boolean;
  chipProps?: Omit<ChipProps, "children">;
};
const BriefProperty = ({ property, fields, showPoolChip = true, chipProps }: Props) => {
  const { t } = useTranslation();

  fields ??= ["pool", "type", "name"];

  const nameColor = showPoolChip ? "default" : getPropertyPoolColor(property?.pool);

  return (
    <div className="flex items-center gap-1">
      {property
        ? fields.map((f) => {
            switch (f) {
              case "pool":
                return showPoolChip ? <PropertyPoolIcon key={f} pool={property.pool} /> : null;
              case "type":
                return <PropertyTypeIcon key={f} type={property.type} />;
              case "name":
                return (
                  <Chip key={f} color={nameColor} radius={"sm"} size="sm" variant="flat" {...chipProps}>
                    {property.name}
                  </Chip>
                );
            }
          })
        : t<string>("Unknown property")}
    </div>
  );
};

BriefProperty.displayName = "BriefProperty";

export default BriefProperty;
