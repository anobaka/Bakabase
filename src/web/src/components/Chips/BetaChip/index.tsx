"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineExperiment } from "react-icons/ai";
import clsx from "clsx";

import { Chip, Tooltip } from "@/components/bakaui";

type Props = {
  className?: string;
  size?: "sm" | "md" | "lg";
  variant?: "solid" | "bordered" | "light" | "flat" | "faded" | "shadow";
  color?: "default" | "primary" | "secondary" | "success" | "warning" | "danger";
  tooltipContent?: string;
  showTooltip?: boolean;
  iconOnly?: boolean;
};

const iconColors = {
  default: "text-default-500",
  primary: "text-primary",
  secondary: "text-secondary",
  success: "text-success",
  warning: "text-warning-700 dark:text-warning",
  danger: "text-danger",
};

const iconSizes = { sm: 14, md: 16, lg: 18 };

const BetaChip = ({
  className,
  size = "sm",
  variant = "flat",
  color = "warning",
  tooltipContent,
  showTooltip = true,
  iconOnly = false,
}: Props) => {
  const { t } = useTranslation();

  const defaultTooltipContent = (
    <>
      {t<string>("BetaFeature.UnstableWarning")}
      <br />
      {t<string>("BetaFeature.BackupWarning")}
    </>
  );

  const chip = iconOnly ? (
    <span
      aria-label={t<string>("Beta")}
      className={clsx("inline-flex shrink-0 items-center", iconColors[color], className)}
      role="img"
    >
      <AiOutlineExperiment aria-hidden size={iconSizes[size]} />
    </span>
  ) : (
    <Chip className={className} color={color} size={size} variant={variant}>
      {t<string>("Beta")}
    </Chip>
  );

  if (!showTooltip) {
    return chip;
  }

  return (
    <Tooltip
      color="foreground"
      content={tooltipContent || defaultTooltipContent}
      delay={iconOnly ? 500 : 2000}
      placement="top"
    >
      {chip}
    </Tooltip>
  );
};

BetaChip.displayName = "BetaChip";

export default BetaChip;
