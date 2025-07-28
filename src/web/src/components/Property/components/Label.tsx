"use client";

import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";

import { Chip } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";

type SimpleProperty = Pick<IProperty, "name" | "type" | "pool">;

interface IProps {
  property: SimpleProperty;
  showPool?: boolean;
}
const Label = ({ property, showPool }: IProps) => {
  const { t } = useTranslation();

  return (
    <div className={"inline-flex items-center"}>
      {showPool && (
        <Chip radius={"sm"} size={"sm"} variant={"flat"}>
          {t<string>(`PropertyPool.${PropertyPool[property.pool]}`)}
        </Chip>
      )}
      <PropertyTypeIcon textVariant={"none"} type={property.type} />
      <span>{property.name}</span>
    </div>
  );
};

Label.displayName = "Label";

export default Label;
