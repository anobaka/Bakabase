"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { PathMarkType, PropertyValueType, PathMatchMode, PathMarkApplyScope } from "@/sdk/constants";

type Props = {
  mark: BakabaseAbstractionsModelsDomainPathMark;
  className?: string;
  /** Mark type label (e.g., "Resource", "Property", "Media Library") */
  label?: string;
  /** Priority number to display as subscript */
  priority?: number;
};

const MarkDescription = ({ mark, className, label, priority }: Props) => {
  const { t } = useTranslation();

  const config = useMemo(() => {
    try {
      return JSON.parse(mark.configJson || "{}");
    } catch {
      return {};
    }
  }, [mark.configJson]);

  const getDescription = (): string => {
    try {
      const matchMode = config.matchMode;
      const parts: string[] = [];

      // Build type label with additional info
      let typeLabel = label || "";

      // For property marks, append property name
      if (mark.type === PathMarkType.Property && mark.property?.name) {
        typeLabel = `${typeLabel}:${mark.property.name}`;
      }

      // For media library marks with fixed value, append library name
      if (mark.type === PathMarkType.MediaLibrary) {
        const valueType = config.valueType ?? PropertyValueType.Fixed;
        if (valueType === PropertyValueType.Fixed && mark.mediaLibrary?.name) {
          typeLabel = `${typeLabel}:${mark.mediaLibrary.name}`;
        }
      }

      if (typeLabel) {
        // Add priority as subscript if > 0
        const prioritySuffix = priority && priority > 0 ? `₍${priority}₎` : "";
        parts.push(`[${typeLabel}]${prioritySuffix}`);
      }

      // Match mode description - simplified layer format
      const applyScope = config.applyScope ?? PathMarkApplyScope.MatchedOnly;
      const includesSubdirs = applyScope === PathMarkApplyScope.MatchedAndSubdirectories;

      if (matchMode === PathMatchMode.Layer) {
        const layer = config.layer ?? 0;
        if (layer === 0) {
          if (includesSubdirs) {
            parts.push(t("MarkDescription.Layer.CurrentAndSubdirs"));
          } else {
            parts.push(t("MarkDescription.Layer.Current"));
          }
        } else if (layer > 0) {
          const layerText = `+${layer}${t("MarkDescription.Layer.Suffix")}`;
          parts.push(includesSubdirs ? `${layerText}${t("MarkDescription.AndSubdirs")}` : layerText);
        } else {
          const layerText = `${layer}${t("MarkDescription.Layer.Suffix")}`;
          parts.push(includesSubdirs ? `${layerText}${t("MarkDescription.AndSubdirs")}` : layerText);
        }
      } else if (matchMode === PathMatchMode.Regex) {
        const regex = config.regex ?? "";
        const regexText = t("MarkDescription.Regex", { regex });
        parts.push(includesSubdirs ? `${regexText}${t("MarkDescription.AndSubdirs")}` : regexText);
      } else if (includesSubdirs) {
        // No match mode but has subdirs scope
        parts.push(t("MarkDescription.Layer.CurrentAndSubdirs"));
      }

      // For property marks - value info
      if (mark.type === PathMarkType.Property) {
        const valueType = config.valueType;
        if (valueType === PropertyValueType.Fixed) {
          const fixedValue = config.fixedValue;
          if (fixedValue !== undefined && fixedValue !== null) {
            parts.push(`="${fixedValue}"`);
          }
        } else if (valueType === PropertyValueType.Dynamic) {
          // Dynamic mode: check valueLayer and valueRegex to determine extraction method
          const valueLayer = config.valueLayer;
          const valueRegex = config.valueRegex;

          if (valueLayer !== undefined && valueLayer !== null) {
            // Layer-based extraction
            if (valueLayer === 0) {
              parts.push(t("MarkDescription.ValueLayer.Current"));
            } else if (valueLayer > 0) {
              parts.push(`${t("MarkDescription.ValueFrom")}+${valueLayer}${t("MarkDescription.Layer.Suffix")}`);
            } else {
              parts.push(`${t("MarkDescription.ValueFrom")}${valueLayer}${t("MarkDescription.Layer.Suffix")}`);
            }
          } else if (valueRegex) {
            // Regex-based extraction
            parts.push(t("MarkDescription.ValueRegex", { regex: valueRegex }));
          }
        }
      }

      // For media library marks - value info
      if (mark.type === PathMarkType.MediaLibrary) {
        const valueType = config.valueType ?? PropertyValueType.Fixed;
        if (valueType === PropertyValueType.Fixed) {
          // Fixed mode: library name is already shown in the label
        } else if (valueType === PropertyValueType.Dynamic) {
          // Dynamic mode: check layerToMediaLibrary and regexToMediaLibrary
          const layerToMediaLibrary = config.layerToMediaLibrary;
          const regexToMediaLibrary = config.regexToMediaLibrary;

          if (layerToMediaLibrary !== undefined && layerToMediaLibrary !== null) {
            // Layer-based extraction
            if (layerToMediaLibrary === 0) {
              parts.push(t("MarkDescription.MediaLibraryLayer.Current"));
            } else if (layerToMediaLibrary > 0) {
              parts.push(`${t("MarkDescription.MediaLibraryFrom")}+${layerToMediaLibrary}${t("MarkDescription.Layer.Suffix")}`);
            } else {
              parts.push(`${t("MarkDescription.MediaLibraryFrom")}${layerToMediaLibrary}${t("MarkDescription.Layer.Suffix")}`);
            }
          } else if (regexToMediaLibrary) {
            // Regex-based extraction
            parts.push(t("MarkDescription.MediaLibraryRegex", { regex: regexToMediaLibrary }));
          }
        }
      }

      // For resource marks
      if (mark.type === PathMarkType.Resource) {
        const fsTypeFilter = config.fsTypeFilter;
        if (fsTypeFilter === 1) {
          parts.push(t("MarkDescription.FileOnly"));
        } else if (fsTypeFilter === 2) {
          parts.push(t("MarkDescription.DirOnly"));
        }

        const extensions = config.extensions;
        if (extensions && extensions.length > 0) {
          parts.push(extensions.join(","));
        }
      }

      return parts.length > 0 ? parts.join(" | ") : t("MarkDescription.Empty");
    } catch (error) {
      console.error("Failed to parse mark config:", error);
      return t("MarkDescription.Invalid");
    }
  };

  return <span className={className}>{getDescription()}</span>;
};

MarkDescription.displayName = "MarkDescription";

export default MarkDescription;
