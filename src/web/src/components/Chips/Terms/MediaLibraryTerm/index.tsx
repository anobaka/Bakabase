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
    <div className="space-y-2">
      <p>{t("Associate matched resources with a media library")}</p>
      <p>{t("Resources will be linked to the selected media library for organization")}</p>
      <p className="text-secondary text-xs pt-1 border-t border-default-200">
        {t("MediaLibrary.Term.Suggestion")}
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
      label={t("Media Library")}
      size={size}
      variant={variant}
    />
  );
};

MediaLibraryTerm.displayName = "MediaLibraryTerm";

export default MediaLibraryTerm;
