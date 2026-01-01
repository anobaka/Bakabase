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
const BetaChip = ({
  className,
  size = "sm",
  variant = "flat",
  color = "warning",
  tooltipContent,
  showTooltip = true,
}: Props) => {
  const { t } = useTranslation();

  const defaultTooltipContent = (
    <>
      {t<string>(
        "This feature is in beta and may be unstable. Please report any issues you encounter.",
      )}
      <br />
      {t<string>("BetaFeature.BackupWarning")}
    </>
  );

  const chip = (
    <Chip className={className} color={color} size={size} variant={variant}>
      {t<string>("Beta")}
    </Chip>
  );

  if (!showTooltip) {
    return chip;
  }

  return (
    <Tooltip
      delay={2000}
      color="foreground"
      content={tooltipContent || defaultTooltipContent}
      placement="top"
    >
      {chip}
    </Tooltip>
  );
};

BetaChip.displayName = "BetaChip";

export default BetaChip;
