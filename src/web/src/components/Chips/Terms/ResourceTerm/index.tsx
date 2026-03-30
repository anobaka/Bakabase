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

export const ResourceDescription = () => {
  const { t } = useTranslation();

  return (
    <div className="space-y-1.5">
      <p>{t("term.resource.description")}</p>
      <p className="text-default-500">{t("term.resource.usage")}</p>
      <p className="text-small text-warning-500 border-t border-default-200 pt-1.5">
        {t("term.resource.tip")}
      </p>
    </div>
  );
};

ResourceDescription.displayName = "ResourceDescription";

const ResourceTerm = ({
  className,
  size = "sm",
  variant = "light",
  color = "success",
}: Props) => {
  const { t } = useTranslation();

  return (
    <TermChip
      className={className}
      color={color}
      description={<ResourceDescription />}
      label={t("common.label.resource")}
      size={size}
      variant={variant}
    />
  );
};

ResourceTerm.displayName = "ResourceTerm";

export default ResourceTerm;
