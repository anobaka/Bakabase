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
const DevelopingChip = ({
  className,
  size = "sm",
  variant = "flat",
  color = "secondary",
  tooltipContent,
  showTooltip = true,
}: Props) => {
  const { t } = useTranslation();

  const defaultTooltipContent = (
    <>
      {t<string>(
        "This feature is currently in development and may not be fully functional.",
      )}
      <br />
      {t<string>("DevelopingFeature.ExpectChanges")}
    </>
  );

  const chip = (
    <Chip className={className} color={color} size={size} variant={variant}>
      {t<string>("Developing")}
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

DevelopingChip.displayName = "DevelopingChip";

export default DevelopingChip;
