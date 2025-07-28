"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";

type Props = {
  className?: string;
  size?: "sm" | "md" | "lg";
  variant?: "solid" | "bordered" | "light" | "flat" | "faded" | "shadow";
  color?:
    | "default"
    | "primary"
    | "secondary"
    | "success"
    | "warning"
    | "danger";
  tooltipContent?: string;
  showTooltip?: boolean;
};
const DeprecatedChip = ({
  className,
  size = "sm",
  variant = "flat",
  color = "danger",
  tooltipContent,
  showTooltip = true,
}: Props) => {
  const { t } = useTranslation();

  const defaultTooltipContent = t<string>(
    "This feature is deprecated and may be removed in future versions. Please consider using the recommended alternative.",
  );

  const chip = (
    <Chip className={className} color={color} size={size} variant={variant}>
      {t<string>("Deprecated")}
    </Chip>
  );

  if (!showTooltip) {
    return chip;
  }

  return (
    <Tooltip
      color="foreground"
      content={tooltipContent || defaultTooltipContent}
      placement="top"
    >
      {chip}
    </Tooltip>
  );
};

DeprecatedChip.displayName = "DeprecatedChip";

export default DeprecatedChip;
