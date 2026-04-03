"use client";

import type { CSSProperties } from "react";

import { Chip } from "@/components/bakaui";
import { autoBackgroundColor } from "@/components/utils";

export interface SelectableChipProps {
  /** Unique key for the chip */
  itemKey: string;
  /** Display label */
  label: React.ReactNode;
  /** Whether this chip is currently selected */
  isSelected: boolean;
  /** Whether this chip is disabled */
  isDisabled?: boolean;
  /** Custom color for the chip (optional) */
  color?: string;
  /** Size of the chip */
  size?: "sm" | "md" | "lg";
  /** Click handler - if not provided, chip is not clickable */
  onClick?: () => void;
}

/**
 * A reusable chip component for selectable options in value renderers.
 * Uses HeroUI Chip for consistent UI styling.
 *
 * States:
 * - Selected: solid style with primary color (or custom color background)
 * - Unselected: flat style with faded appearance
 * - Disabled: bordered style with reduced opacity, not clickable
 */
const SelectableChip = ({
  itemKey,
  label,
  isSelected,
  isDisabled,
  color,
  size,
  onClick,
}: SelectableChipProps) => {
  const style: CSSProperties = {};
  const classNames: string[] = [];

  if (onClick && !isDisabled) {
    classNames.push("cursor-pointer");
  }

  // Determine variant and color based on state
  let chipVariant: "solid" | "flat" | "bordered" | "light";
  let chipColor: "default" | "primary" | undefined;

  if (isDisabled) {
    chipVariant = "bordered";
    chipColor = "default";
    classNames.push("opacity-40");
    if (color) {
      style.color = color;
      style.borderColor = color;
    }
  } else if (isSelected) {
    if (color) {
      chipVariant = "flat";
      style.color = color;
      style.backgroundColor = autoBackgroundColor(color);
    } else {
      chipVariant = "solid";
      chipColor = "primary";
    }
  } else {
    chipVariant = "flat";
    chipColor = "default";
    classNames.push("opacity-50");
    if (color) {
      style.color = color;
    }
  }

  return (
    <Chip
      key={itemKey}
      size={size}
      variant={chipVariant}
      color={chipColor}
      className={classNames.join(" ")}
      style={Object.keys(style).length > 0 ? style : undefined}
      isDisabled={isDisabled}
      onClick={isDisabled ? undefined : onClick}
    >
      {label}
    </Chip>
  );
};

SelectableChip.displayName = "SelectableChip";

export default SelectableChip;
