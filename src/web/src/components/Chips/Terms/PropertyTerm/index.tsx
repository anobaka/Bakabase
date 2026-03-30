"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import TermChip from "../TermChip";

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

export const PropertyDescription = () => {
  const { t } = useTranslation();

  return (
    <div className="space-y-1.5">
      <p>{t("term.property.description")}</p>
      <p className="text-default-500">{t("term.property.usage")}</p>
      <p className="text-small text-warning-500 border-t border-default-200 pt-1.5">
        {t("term.property.tip")}
      </p>
    </div>
  );
};

PropertyDescription.displayName = "PropertyDescription";

const PropertyTerm = ({
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
      description={<PropertyDescription />}
      label={t("common.label.property")}
      size={size}
      variant={variant}
    />
  );
};

PropertyTerm.displayName = "PropertyTerm";

export default PropertyTerm;
