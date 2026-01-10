"use client";

import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { Button } from "@/components/bakaui";

export interface DisableButtonProps {
  disabled?: boolean;
  onToggle?: () => void;
}

/**
 * Small disable/enable button for filters.
 * Shows different icon based on current disabled state.
 */
const DisableButton = ({ disabled, onToggle }: DisableButtonProps) => {
  return (
    <Button
      isIconOnly
      size="sm"
      variant="light"
      color={disabled ? "success" : "warning"}
      className="min-w-6 w-6 h-6"
      onPress={onToggle}
    >
      {disabled ? (
        <MdOutlineFilterAlt className="text-sm" />
      ) : (
        <MdOutlineFilterAltOff className="text-sm" />
      )}
    </Button>
  );
};

DisableButton.displayName = "DisableButton";

export default DisableButton;
