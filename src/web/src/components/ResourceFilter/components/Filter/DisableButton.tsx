"use client";

import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";
import { useTranslation } from "react-i18next";

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
  const { t } = useTranslation();
  const label = t<string>(
    disabled ? "resourceFilter.condition.enable" : "resourceFilter.condition.disable",
  );

  return (
    <Button
      isIconOnly
      aria-label={label}
      className="h-8 w-8 min-w-8 shrink-0"
      color={disabled ? "warning" : "default"}
      size="sm"
      title={label}
      variant="light"
      onPress={onToggle}
    >
      {disabled ? (
        <MdOutlineFilterAlt aria-hidden className="text-base" />
      ) : (
        <MdOutlineFilterAltOff aria-hidden className="text-base" />
      )}
    </Button>
  );
};

DisableButton.displayName = "DisableButton";

export default DisableButton;
