"use client";

import type { CSSProperties } from "react";

import { Chip } from "@/components/bakaui";

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
  /** Click handler */
  onClick: () => void;
}

/**
 * A reusable chip component for selectable options in value renderers.
 *
 * Styling logic:
 * - If item has custom color:
 *   - Unselected: show text in custom color, light variant
 *   - Selected: use custom color as background, theme text color
 * - If no custom color:
 *   - Unselected: light variant, default color
 *   - Selected: solid variant, primary color
 */
const SelectableChip = ({
  itemKey,
  label,
  isSelected,
  color,
  size,
  onClick,
}: SelectableChipProps) => {
  const style: CSSProperties | undefined = color
    ? isSelected
      ? { backgroundColor: color }
      : { color: color }
    : undefined;

  return (
    <Chip
      key={itemKey}
      className="h-auto whitespace-break-spaces py-1 cursor-pointer"
      radius="sm"
      size={size}
      variant={isSelected ? "solid" : "light"}
      color={color ? "default" : (isSelected ? "primary" : "default")}
      style={style}
      onClick={onClick}
    >
      {label}
    </Chip>
  );
};

SelectableChip.displayName = "SelectableChip";

export default SelectableChip;
