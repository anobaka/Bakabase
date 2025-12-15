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

const ResourceTerm = ({
  className,
  size = "sm",
  variant = "light",
  color = "success",
}: Props) => {
  const { t } = useTranslation();

  const description = (
    <div className="space-y-2">
      <p>{t("Term.Resource.Description")}</p>
      <p>{t("Term.Resource.Usage")}</p>
      <p className="text-small text-warning-500">
        {t("Term.Resource.Tip")}
      </p>
    </div>
  );

  return (
    <TermChip
      className={className}
      color={color}
      description={description}
      label={t("Resource")}
      size={size}
      variant={variant}
    />
  );
};

ResourceTerm.displayName = "ResourceTerm";

export default ResourceTerm;
