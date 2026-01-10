"use client";

import type { ChipProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";

export const getPropertyPoolColor = (pool?: PropertyPool): ChipProps["color"] => {
  switch (pool) {
    case PropertyPool.Internal:
      return "default";
    case PropertyPool.Reserved:
      return "secondary";
    case PropertyPool.Custom:
      return "primary";
    default:
      return "warning";
  }
};

type Props = {
  pool?: PropertyPool;
};
const PropertyPoolIcon = ({ pool }: Props) => {
  const { t } = useTranslation();

  const color = getPropertyPoolColor(pool);

  const poolName = pool
    ? t<string>(`PropertyPool.${PropertyPool[pool]}`)
    : t<string>("Unknown");
  const poolAbbreviation = pool
    ? t<string>(`PropertyPool.Abbreviation.${PropertyPool[pool]}`)
    : "?";

  return (
    <Tooltip color={"foreground"} content={poolName}>
      <Chip color={color} size={"sm"} variant={"flat"}>
        {poolAbbreviation}
      </Chip>
    </Tooltip>
  );
};

PropertyPoolIcon.displayName = "PropertyPoolIcon";

export default PropertyPoolIcon;
