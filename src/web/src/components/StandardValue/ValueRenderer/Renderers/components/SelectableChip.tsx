"use client";

import type { CSSProperties } from "react";

import { sizeTextClassMap } from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import { autoBackgroundColor } from "@/components/utils";

export interface SelectableChipProps {
  /** Unique key for the chip */
  itemKey: string;
  /** Display label */
  label: React.ReactNode;
  /** Whether this chip is currently selected */
  isSelected: boolean;
  /** Custom color for the chip (optional) */
  color?: string;
  /** Size of the chip */
  size?: "sm" | "md" | "lg";
  /** Click handler - if not provided, chip is not clickable */
  onClick?: () => void;
}

/**
 * A reusable chip component for selectable options in value renderers.
 * Uses minimal padding to align with LightText components.
 *
 * Styling logic:
 * - If item has custom color:
 *   - Unselected: show text in custom color, faded
 *   - Selected: text in custom color with auto-generated light background
 * - If no custom color:
 *   - Unselected: faded text
 *   - Selected: primary background
 */
const SelectableChip = ({
  itemKey,
  label,
  isSelected,
  color,
  size,
  onClick,
}: SelectableChipProps) => {
  const textClass = size ? sizeTextClassMap[size] : sizeTextClassMap.md;

  // Build styles based on selection state and custom color
  const style: CSSProperties = {};
  const classNames: string[] = [
    "inline-flex items-center rounded-md px-1",
    textClass,
  ];

  if (onClick) {
    classNames.push("cursor-pointer");
  }

  if (isSelected) {
    if (color) {
      // Selected with custom color: text in custom color with light background
      style.color = color;
      style.backgroundColor = autoBackgroundColor(color);
    } else {
      // Selected without custom color: primary background
      classNames.push("bg-primary text-primary-foreground");
    }
  } else {
    if (color) {
      // Unselected with custom color: show text in custom color, faded
      style.color = color;
      classNames.push("opacity-50");
    } else {
      // Unselected without custom color: faded text
      classNames.push("opacity-50");
    }
  }

  return (
    <span
      key={itemKey}
      className={classNames.join(" ")}
      style={Object.keys(style).length > 0 ? style : undefined}
      onClick={onClick}
    >
      {label}
    </span>
  );
};

SelectableChip.displayName = "SelectableChip";

export default SelectableChip;
