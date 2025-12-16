"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React from "react";
import { useTranslation } from "react-i18next";
import { PathMarkType } from "@/sdk/constants";

type Props = {
  mark: BakabaseAbstractionsModelsDomainPathMark;
  className?: string;
};

const MarkDescription = ({ mark, className }: Props) => {
  const { t } = useTranslation();

  const getDescription = (): string => {
    try {
      const config = JSON.parse(mark.configJson || "{}");
      const isResource = mark.type === PathMarkType.Resource;
      const matchMode = config.MatchMode;

      let description = "";

      // Match mode description
      if (matchMode === "Layer") {
        const layer = config.Layer ?? 0;
        if (layer === 0) {
          description = t("MarkDescription.Layer.Current");
        } else {
          description = t("MarkDescription.Layer.Down", { layer });
        }
      } else if (matchMode === "Regex") {
        const regex = config.Regex ?? "";
        description = t("MarkDescription.Regex", { regex });
      }

      // For property marks, add value information
      if (!isResource) {
        const valueType = config.ValueType;
        if (valueType === "Fixed") {
          const fixedValue = config.FixedValue;
          if (fixedValue) {
            description += t("MarkDescription.FixedValue", { value: fixedValue });
          }
        } else if (valueType === "Dynamic") {
          const valueLayer = config.ValueLayer;
          const valueRegex = config.ValueRegex;
          if (valueLayer !== undefined) {
            description += t("MarkDescription.ValueLayer", { layer: valueLayer });
          }
          if (valueRegex) {
            description += t("MarkDescription.ValueRegex", { regex: valueRegex });
          }
        }
      }

      // Add file type filter for resource marks
      if (isResource) {
        const fsTypeFilter = config.FsTypeFilter;
        if (fsTypeFilter) {
          description += t("MarkDescription.FileType", { type: fsTypeFilter });
        }

        const extensions = config.Extensions;
        if (extensions && extensions.length > 0) {
          description += t("MarkDescription.Extensions", { ext: extensions.join(", ") });
        }
      }

      return description || t("MarkDescription.Empty");
    } catch (error) {
      console.error("Failed to parse mark config:", error);
      return t("MarkDescription.Invalid");
    }
  };

  return <span className={className}>{getDescription()}</span>;
};

MarkDescription.displayName = "MarkDescription";

export default MarkDescription;
