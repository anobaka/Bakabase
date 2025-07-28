"use client";

import type { ChipProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";

type Props = {
  pool?: PropertyPool;
};
const PropertyPoolIcon = ({ pool }: Props) => {
  const { t } = useTranslation();

  let color: ChipProps["color"] = "default";

  switch (pool) {
    case PropertyPool.Internal:
      color = "default";
      break;
    case PropertyPool.Reserved:
      color = "secondary";
      break;
    case PropertyPool.Custom:
      color = "primary";
      break;
    default:
      color = "warning";
      break;
  }

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
