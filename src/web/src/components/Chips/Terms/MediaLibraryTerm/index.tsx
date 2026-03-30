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

export const MediaLibraryDescription = () => {
  const { t } = useTranslation();

  return (
    <div className="space-y-1.5">
      <p>{t("term.mediaLibrary.description")}</p>
      <p className="text-default-500">{t("term.mediaLibrary.usage")}</p>
      <p className="text-small text-warning-500 border-t border-default-200 pt-1.5">
        {t("term.mediaLibrary.tip")}
      </p>
    </div>
  );
};

MediaLibraryDescription.displayName = "MediaLibraryDescription";

const MediaLibraryTerm = ({
  className,
  size = "sm",
  variant = "light",
  color = "secondary",
}: Props) => {
  const { t } = useTranslation();

  return (
    <TermChip
      className={className}
      color={color}
      description={<MediaLibraryDescription />}
      label={t("common.label.mediaLibrary")}
      size={size}
      variant={variant}
    />
  );
};

MediaLibraryTerm.displayName = "MediaLibraryTerm";

export default MediaLibraryTerm;
