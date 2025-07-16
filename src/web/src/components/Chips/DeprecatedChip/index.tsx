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

/**
 * DeprecatedChip component for indicating deprecated features
 *
 * @example
 * // Basic usage
 * <DeprecatedChip />
 *
 * @example
 * // Custom styling
 * <DeprecatedChip size="md" color="danger" variant="solid" />
 *
 * @example
 * // Custom tooltip
 * <DeprecatedChip tooltipContent="This feature will be removed in the next version" />
 *
 * @example
 * // Without tooltip
 * <DeprecatedChip showTooltip={false} />
 *
 * @example
 * // Different colors for different deprecation types
 * <DeprecatedChip color="danger" /> // Critical deprecation
 * <DeprecatedChip color="warning" /> // Warning deprecation
 * <DeprecatedChip color="secondary" /> // Soft deprecation
 */
export default ({
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
