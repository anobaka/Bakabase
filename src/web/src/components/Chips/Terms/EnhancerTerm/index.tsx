"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import TermChip from "../TermChip";
import { EnhancerDescription } from "./Description";

export { EnhancerDescription } from "./Description";

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
};

const EnhancerTerm = ({
  className,
  size = "sm",
  variant = "light",
  color = "primary",
}: Props) => {
  const { t } = useTranslation();

  return (
    <TermChip
      className={className}
      color={color}
      description={<EnhancerDescription />}
      label={t("common.label.enhancer")}
      size={size}
      variant={variant}
    />
  );
};

EnhancerTerm.displayName = "EnhancerTerm";

export default EnhancerTerm;
