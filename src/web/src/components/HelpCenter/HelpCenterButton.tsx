"use client";

import type { HelpTarget } from "./types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import HelpCenterModal from "./HelpCenterModal";

import { Button, Tooltip } from "@/components/bakaui";

export interface HelpCenterButtonProps extends HelpTarget {
  /** When set, renders a labelled button instead of the icon-only "?" button. */
  label?: string;
  size?: "sm" | "md";
  className?: string;
}

/**
 * The "?" entry point. Drop it next to any UI that involves a help center
 * topic; it opens the help center at the given topic/section.
 */
const HelpCenterButton = ({
  topic,
  section,
  concept,
  label,
  size = "sm",
  className,
}: HelpCenterButtonProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  const button = label ? (
    <Button
      className={className}
      size={size}
      startContent={<AiOutlineQuestionCircle className="text-base" />}
      variant="light"
      onPress={() => setVisible(true)}
    >
      {label}
    </Button>
  ) : (
    <Tooltip content={t("helpCenter.button.tooltip")}>
      <Button
        isIconOnly
        aria-label={t("helpCenter.button.tooltip")}
        className={className}
        size={size}
        variant="light"
        onPress={() => setVisible(true)}
      >
        <AiOutlineQuestionCircle className="text-lg text-default-500" />
      </Button>
    </Tooltip>
  );

  return (
    <>
      {button}
      {visible && (
        <HelpCenterModal
          concept={concept}
          section={section}
          topic={topic}
          visible={visible}
          onClose={() => setVisible(false)}
        />
      )}
    </>
  );
};

HelpCenterButton.displayName = "HelpCenterButton";

export default HelpCenterButton;
